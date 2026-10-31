using System.Text.Json;
using DevArv.Contracts;
using DevArv.Web.Data;
using DevArv.Web.Domain;
using DevArv.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace DevArv.Web.Pages.Tasks;

public sealed class DetailsModel(
    AppDbContext db, WorkflowService workflow, AttachmentStorage storage, Clock clock) : PageModel
{
    [BindProperty(SupportsGet = true)] public int Id { get; set; }
    [BindProperty(SupportsGet = true)] public bool Modal { get; set; }
    [BindProperty(SupportsGet = true, Name = "run")] public long? RunId { get; set; }

    public TaskItem Item { get; private set; } = null!;
    public List<Stage> Stages { get; private set; } = [];
    public List<WorkerNode> Workers { get; private set; } = [];
    public TaskRun? SelectedRun { get; private set; }
    public List<ChangedFile> SelectedRunFiles { get; private set; } = [];
    public Clock Clock => clock;
    public string? Error => TempData["Error"] as string;
    public string? Notice => TempData["Notice"] as string;
    public bool IsLive => Item.Status is WorkStatus.Queued or WorkStatus.Running;
    public int MaxFiles => storage.Options.MaxFilesPerTask;

    public string SelfUrl(long? runId = null)
    {
        var url = $"/Tasks/Details/{Id}?modal={(Modal ? "true" : "false")}";
        var run = runId ?? RunId;
        return run is null ? url : $"{url}&run={run}";
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!await LoadAsync(ct)) return NotFound();
        if (Modal) ViewData["Modal"] = true;
        return Page();
    }

    /// <summary>Muestra un adjunto. El HTML siempre se descarga y con CSP sandbox para que nunca se ejecute en este dominio.</summary>
    public async Task<IActionResult> OnGetAttachmentAsync(long attachmentId, CancellationToken ct)
    {
        var a = await db.TaskAttachments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == attachmentId && x.TaskId == Id, ct);
        if (a is null) return NotFound();
        var path = storage.PathOf(a);
        if (!System.IO.File.Exists(path)) return NotFound();

        Response.Headers.XContentTypeOptions = "nosniff";
        if (a.Kind != AttachmentKind.Pdf)
            Response.Headers.ContentSecurityPolicy = "sandbox; default-src 'none'; img-src 'self'; style-src 'unsafe-inline'";
        if (a.Kind == AttachmentKind.Html)
            return new PhysicalFileResult(path, "application/octet-stream") { FileDownloadName = a.FileName };
        return new PhysicalFileResult(path, a.ContentType) { EnableRangeProcessing = true };
    }

    public Task<IActionResult> OnPostApproveAsync(CancellationToken ct) =>
        ActAsync(t => workflow.ApproveAsync(t, Actor, ct), "Aprobado.", ct);

    public Task<IActionResult> OnPostRequestChangesAsync(string feedback, CancellationToken ct) =>
        ActAsync(t => workflow.RequestChangesAsync(t, Actor, feedback, ct), "Cambios solicitados.", ct);

    public Task<IActionResult> OnPostCommentAsync(string body, bool asFeedback, CancellationToken ct) =>
        ActAsync(t => workflow.AddCommentAsync(t, Actor, body, asFeedback, ct), "Comentario agregado.", ct);

    public Task<IActionResult> OnPostRetryAsync(CancellationToken ct) =>
        ActAsync(t => workflow.RetryAsync(t, Actor, ct), "Ejecución encolada de nuevo.", ct);

    public Task<IActionResult> OnPostCancelAsync(CancellationToken ct) =>
        ActAsync(t => workflow.CancelAsync(t, Actor, ct), "Cancelación solicitada.", ct);

    public Task<IActionResult> OnPostMoveAsync(int stageId, CancellationToken ct) =>
        ActAsync(t => workflow.MoveAsync(t, stageId, Actor, ct), "Tarea movida.", ct);

    public Task<IActionResult> OnPostArchiveAsync(bool archived, CancellationToken ct) =>
        ActAsync(t => workflow.SetArchivedAsync(t, archived, Actor, ct), archived ? "Tarea archivada." : "Tarea restaurada.", ct);

    public Task<IActionResult> OnPostUpdateAsync(
        Priority priority, Complexity complexity, string? labels, string? modelOverride, int? preferredWorkerId,
        string? title, string? description, CancellationToken ct) =>
        ActAsync(async t =>
        {
            if (modelOverride is not null && !System.Text.RegularExpressions.Regex.IsMatch(modelOverride.Trim(), @"^[A-Za-z0-9.\-_\[\]]*$"))
                throw new WorkflowException("Nombre de modelo inválido.");
            if (preferredWorkerId is not null && !await db.Workers.AnyAsync(w => w.Id == preferredWorkerId, ct))
                throw new WorkflowException("La PC no existe.");
            if (!string.IsNullOrWhiteSpace(title)) t.Title = title.Trim()[..Math.Min(title.Trim().Length, 200)];
            if (description is not null) t.Description = description.Trim();
            t.Priority = priority;
            t.Complexity = complexity;
            t.Labels = CreateModel.NormalizeLabels(labels);
            t.ModelOverride = string.IsNullOrWhiteSpace(modelOverride) ? null : modelOverride.Trim();
            t.PreferredWorkerId = preferredWorkerId;
            t.UpdatedAt = DateTimeOffset.UtcNow;
            workflow.AddEvent(t.Id, Actor, $"{Actor} editó la tarea");
            await db.SaveChangesAsync(ct);
        }, "Cambios guardados.", ct);

    public Task<IActionResult> OnPostUploadAsync(List<IFormFile> files, CancellationToken ct) =>
        ActAsync(async t =>
        {
            var existing = await db.TaskAttachments.CountAsync(a => a.TaskId == t.Id, ct);
            try
            {
                db.TaskAttachments.AddRange(await storage.SaveAsync(t.Id, files, existing, ct));
            }
            catch (AttachmentValidationException ex)
            {
                throw new WorkflowException(ex.Message);
            }
            workflow.AddEvent(t.Id, Actor, $"{Actor} adjuntó {files.Count} archivo(s)");
            await db.SaveChangesAsync(ct);
        }, "Archivos adjuntados.", ct);

    public Task<IActionResult> OnPostDeleteAttachmentAsync(long attachmentId, CancellationToken ct) =>
        ActAsync(async t =>
        {
            var a = await db.TaskAttachments.FirstOrDefaultAsync(x => x.Id == attachmentId && x.TaskId == t.Id, ct)
                    ?? throw new WorkflowException("El adjunto no existe.");
            db.TaskAttachments.Remove(a);
            workflow.AddEvent(t.Id, Actor, $"{Actor} eliminó el adjunto «{a.FileName}»");
            await db.SaveChangesAsync(ct);
            storage.Delete(a);
        }, "Adjunto eliminado.", ct);

    private string Actor => User.Identity?.Name ?? "?";

    private async Task<IActionResult> ActAsync(Func<TaskItem, Task> action, string notice, CancellationToken ct)
    {
        var task = await workflow.LoadTaskAsync(Id, ct);
        if (task is null) return NotFound();
        try
        {
            await action(task);
            TempData["Notice"] = notice;
        }
        catch (WorkflowException ex)
        {
            TempData["Error"] = ex.Message;
        }
        catch (GitHubException ex)
        {
            TempData["Error"] = ex.Message;
        }
        // Desde el modal se vuelve al tablero con la tarea abierta; desde la página, a la página.
        return Redirect(Modal ? $"/{BoardQuery()}" : $"/Tasks/Details/{Id}");
    }

    private string BoardQuery()
    {
        var returnQuery = Request.Form["returnQuery"].ToString();
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(returnQuery);
        var keep = query
            .Where(kv => kv.Key is "project" or "worker" or "waiting" or "compact" or "hideEmpty" or "archived" or "q")
            .ToDictionary(kv => kv.Key, kv => (string?)kv.Value.ToString());
        keep["task"] = Id.ToString();
        return Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString("", keep);
    }

    private async Task<bool> LoadAsync(CancellationToken ct)
    {
        var task = await db.Tasks
            .Include(t => t.Project)
            .Include(t => t.Stage)
            .Include(t => t.PreferredWorker)
            .Include(t => t.Attachments)
            .Include(t => t.Comments)
            .Include(t => t.Events)
            .Include(t => t.Runs).ThenInclude(r => r.Worker)
            .AsSplitQuery()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == Id, ct);
        if (task is null) return false;

        task.Runs = task.Runs.OrderByDescending(r => r.Id).ToList();
        Item = task;
        Stages = await db.Stages.AsNoTracking().OrderBy(s => s.Order).ToListAsync(ct);
        Workers = await db.Workers.AsNoTracking().Where(w => w.IsEnabled).OrderBy(w => w.Name).ToListAsync(ct);
        SelectedRun = task.Runs.FirstOrDefault(r => r.Id == RunId) ?? task.Runs.FirstOrDefault();
        if (SelectedRun?.ChangedFilesJson is { } json)
        {
            try { SelectedRunFiles = JsonSerializer.Deserialize<List<ChangedFile>>(json) ?? []; }
            catch (JsonException) { SelectedRunFiles = []; }
        }
        return true;
    }

    public string HandlerUrl(string handler) => $"/Tasks/Details/{Id}?handler={handler}&modal={(Modal ? "true" : "false")}";

    public string AttachmentUrl(long attachmentId) => $"/Tasks/Details/{Id}?handler=Attachment&attachmentId={attachmentId}";

    public string StageName(int? id) => Stages.FirstOrDefault(s => s.Id == id)?.Name ?? "—";
}
