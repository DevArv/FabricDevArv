using System.Text.Json;
using DevArv.Contracts.Models;
using DevArv.Web.DataModels;
using DevArv.Web.Enums.Tasks;
using DevArv.Web.Helpers;
using DevArv.Web.Repositories;
using DevArv.Web.Services;
using DevArv.Web.ViewModels;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;

namespace DevArv.Web.Pages.Tasks;

public sealed class DetailsModel(
    TasksRepository tasks, StagesRepository stages, WorkersRepository workers, TaskAttachmentsRepository attachments,
    WorkflowService workflow, Clock clock) : PageModel
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
    public int MaxFiles => attachments.Limits.MaxFilesPerTask;

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
        var file = await attachments.GetFileAsync(Id, attachmentId, ct);
        if (file is null) return NotFound();
        var (a, path) = file.Value;

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
        ActAsync(t => tasks.UpdateAsync(
            t, new TaskUpdateViewModel(priority, complexity, labels, modelOverride, preferredWorkerId, title, description), Actor, ct),
            "Cambios guardados.", ct);

    public Task<IActionResult> OnPostUploadAsync(List<IFormFile> files, CancellationToken ct) =>
        ActAsync(t => attachments.AddAsync(t.Id, files, Actor, ct), "Archivos adjuntados.", ct);

    public Task<IActionResult> OnPostDeleteAttachmentAsync(long attachmentId, CancellationToken ct) =>
        ActAsync(t => attachments.DeleteAsync(t.Id, attachmentId, Actor, ct), "Adjunto eliminado.", ct);

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
        catch (ValidationFailedException ex)
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
        var task = await tasks.GetDetailsAsync(Id, ct);
        if (task is null) return false;

        Item = task;
        Stages = await stages.GetAllAsync(ct);
        Workers = await workers.GetEnabledAsync(ct);
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
