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
    TasksRepository TasksRepository, TaskAttachmentsRepository AttachmentsRepository, WorkflowService WorkflowService,
    Clock ClockService) : PageModel
{
    [BindProperty(SupportsGet = true)] public int Id { get; set; }
    [BindProperty(SupportsGet = true)] public bool Modal { get; set; }
    [BindProperty(SupportsGet = true, Name = "run")] public long? RunId { get; set; }

    public TaskDetailsViewModel Details { get; private set; } = null!;
    public TaskItem Item => Details.Item;
    public List<Stage> Stages => Details.Stages;
    public List<WorkerNode> Workers => Details.Workers;
    public TaskRun? SelectedRun => Details.SelectedRun;
    public List<ChangedFile> SelectedRunFiles => Details.SelectedRunFiles;
    public Clock Clock => ClockService;
    public string? Error => TempData["Error"] as string;
    public string? Notice => TempData["Notice"] as string;
    public bool IsLive => Item.Status is WorkStatus.Queued or WorkStatus.Running;
    public int MaxFiles => AttachmentsRepository.Limits.MaxFilesPerTask;

    public string SelfUrl(long? OverrideRunID = null)
    {
        var url = $"/Tasks/Details/{Id}?modal={(Modal ? "true" : "false")}";
        var run = OverrideRunID ?? RunId;
        return run is null ? url : $"{url}&run={run}";
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken CT)
    {
        var details = await TasksRepository.GetDetailsViewAsync(
            ID: Id,
            RunID: RunId,
            CT: CT);
        if (details is null) return NotFound();
        Details = details;
        if (Modal) ViewData["Modal"] = true;
        return Page();
    }

    /// <summary>Muestra un adjunto. El HTML siempre se descarga y con CSP sandbox para que nunca se ejecute en este dominio.</summary>
    public async Task<IActionResult> OnGetAttachmentAsync(long AttachmentID, CancellationToken CT)
    {
        var file = await AttachmentsRepository.GetFileAsync(
            TaskID: Id,
            AttachmentID: AttachmentID,
            CT: CT);
        if (file is null) return NotFound();
        var (attachment, path) = file.Value;

        Response.Headers.XContentTypeOptions = "nosniff";
        if (attachment.Kind != AttachmentKind.Pdf)
            Response.Headers.ContentSecurityPolicy = "sandbox; default-src 'none'; img-src 'self'; style-src 'unsafe-inline'";
        if (attachment.Kind == AttachmentKind.Html)
            return new PhysicalFileResult(path, "application/octet-stream") { FileDownloadName = attachment.FileName };
        return new PhysicalFileResult(path, attachment.ContentType) { EnableRangeProcessing = true };
    }

    public Task<IActionResult> OnPostApproveAsync(CancellationToken CT) =>
        ActAsync(
            Action: item => WorkflowService.ApproveAsync(
                Item: item,
                Actor: Actor,
                CT: CT),
            Notice: "Aprobado.",
            CT: CT);

    public Task<IActionResult> OnPostRequestChangesAsync(string Feedback, CancellationToken CT) =>
        ActAsync(
            Action: item => WorkflowService.RequestChangesAsync(
                Item: item,
                Actor: Actor,
                Feedback: Feedback,
                CT: CT),
            Notice: "Cambios solicitados.",
            CT: CT);

    public Task<IActionResult> OnPostCommentAsync(string Body, bool AsFeedback, CancellationToken CT) =>
        ActAsync(
            Action: item => WorkflowService.AddCommentAsync(
                Item: item,
                Actor: Actor,
                Body: Body,
                AsFeedback: AsFeedback,
                CT: CT),
            Notice: "Comentario agregado.",
            CT: CT);

    public Task<IActionResult> OnPostRetryAsync(CancellationToken CT) =>
        ActAsync(
            Action: item => WorkflowService.RetryAsync(
                Item: item,
                Actor: Actor,
                CT: CT),
            Notice: "Ejecución encolada de nuevo.",
            CT: CT);

    public Task<IActionResult> OnPostCancelAsync(CancellationToken CT) =>
        ActAsync(
            Action: item => WorkflowService.CancelAsync(
                Item: item,
                Actor: Actor,
                CT: CT),
            Notice: "Cancelación solicitada.",
            CT: CT);

    public Task<IActionResult> OnPostMoveAsync(int StageID, CancellationToken CT) =>
        ActAsync(
            Action: item => WorkflowService.MoveAsync(
                Item: item,
                StageID: StageID,
                Actor: Actor,
                CT: CT),
            Notice: "Tarea movida.",
            CT: CT);

    public Task<IActionResult> OnPostArchiveAsync(bool Archived, CancellationToken CT) =>
        ActAsync(
            Action: item => WorkflowService.SetArchivedAsync(
                Item: item,
                Archived: Archived,
                Actor: Actor,
                CT: CT),
            Notice: Archived ? "Tarea archivada." : "Tarea restaurada.",
            CT: CT);

    /// <summary>Elimina la tarea. Si se pudo, vuelve al tablero (sin abrir la tarea); si no, muestra el error en la tarea.</summary>
    public async Task<IActionResult> OnPostDeleteAsync(CancellationToken CT)
    {
        var task = await WorkflowService.LoadTaskAsync(
            TaskID: Id,
            CT: CT);
        if (task is null) return NotFound();
        try
        {
            await TasksRepository.DeleteAsync(
                Item: task,
                Actor: Actor,
                CT: CT);
        }
        catch (WorkflowException ex)
        {
            TempData["Error"] = ex.Message;
            return Redirect(Modal ? $"/{BoardQuery()}" : $"/Tasks/Details/{Id}");
        }
        return Redirect(Modal ? $"/{BoardQuery(OpenTask: false)}" : "/");
    }

    public Task<IActionResult> OnPostUpdateAsync(TaskUpdateViewModel Input, CancellationToken CT) =>
        ActAsync(
            Action: item => TasksRepository.UpdateAsync(
                Item: item,
                Record: Input,
                Actor: Actor,
                CT: CT),
            Notice: "Cambios guardados.",
            CT: CT);

    public Task<IActionResult> OnPostUploadAsync(List<IFormFile> Files, CancellationToken CT) =>
        ActAsync(
            Action: item => AttachmentsRepository.AddAsync(
                TaskID: item.Id,
                Files: Files,
                Actor: Actor,
                CT: CT),
            Notice: "Archivos adjuntados.",
            CT: CT);

    public Task<IActionResult> OnPostDeleteAttachmentAsync(long AttachmentID, CancellationToken CT) =>
        ActAsync(
            Action: item => AttachmentsRepository.DeleteAsync(
                TaskID: item.Id,
                AttachmentID: AttachmentID,
                Actor: Actor,
                CT: CT),
            Notice: "Adjunto eliminado.",
            CT: CT);

    private string Actor => User.Identity?.Name ?? "?";

    private async Task<IActionResult> ActAsync(Func<TaskItem, Task> Action, string Notice, CancellationToken CT)
    {
        // Un valor que el formulario no pudo convertir (p. ej. una prioridad inexistente) llega como el valor por
        // defecto del tipo: si se ejecutara la acción se sobrescribiría el dato guardado sin avisar.
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Alguno de los datos enviados no es válido.";
            return Redirect(Modal ? $"/{BoardQuery()}" : $"/Tasks/Details/{Id}");
        }

        var task = await WorkflowService.LoadTaskAsync(
            TaskID: Id,
            CT: CT);
        if (task is null) return NotFound();
        try
        {
            await Action(task);
            TempData["Notice"] = Notice;
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

    private string BoardQuery(bool OpenTask = true)
    {
        var returnQuery = Request.Form["returnQuery"].ToString();
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(returnQuery);
        var keep = query
            .Where(kv => kv.Key is "project" or "worker" or "waiting" or "compact" or "hideEmpty" or "archived" or "q")
            .ToDictionary(kv => kv.Key, kv => (string?)kv.Value.ToString());
        if (OpenTask) keep["task"] = Id.ToString();
        return Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString("", keep);
    }

    public string HandlerUrl(string Handler) => $"/Tasks/Details/{Id}?handler={Handler}&modal={(Modal ? "true" : "false")}";

    public string AttachmentUrl(long AttachmentID) => $"/Tasks/Details/{Id}?handler=Attachment&attachmentId={AttachmentID}";

    public string StageName(int? ID) => Stages.FirstOrDefault(s => s.Id == ID)?.Name ?? "—";
}
