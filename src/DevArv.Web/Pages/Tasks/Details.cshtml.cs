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
    // Estado que usan la vista y los enlaces; cada handler lo asigna con los valores que recibe de la URL.
    public int ID { get; private set; }
    public bool Modal { get; private set; }
    public long? RunID { get; private set; }

    public TaskDetailsViewModel Details { get; private set; } = null!;
    public TaskItem Item => Details.Item;
    public List<Stage> Stages => Details.Stages;
    public List<WorkerNode> Workers => Details.Workers;
    public TaskRun? SelectedRun => Details.SelectedRun;
    public List<ChangedFile> SelectedRunFiles => Details.SelectedRunFiles;
    public bool ShowConflicts => Details.ShowConflicts;
    public bool CanResolveConflicts => Details.CanResolveConflicts;
    public bool ResolvingConflicts => Details.ResolvingConflicts;
    public List<TaskAttachment> Attachments => Details.Attachments;
    public List<TaskAttachment> Captures => Details.Captures;
    public bool ShowReportTabs => Details.ShowReportTabs;
    public int SelectedRunLogLines => Details.SelectedRunLogLines;
    public TaskReportSectionViewModel? UnitTests => Details.UnitTests;
    public TaskReportSectionViewModel? QaTests => Details.QaTests;
    public Clock Clock => ClockService;
    public string? Error => TempData["Error"] as string;
    public string? Notice => TempData["Notice"] as string;
    public bool IsLive => Item.Status is WorkStatus.QUEUED or WorkStatus.RUNNING;
    public int MaxFiles => AttachmentsRepository.Limits.MaxFilesPerTask;

    public string SelfUrl(long? OverrideRunID = null)
    {
        var url = $"/Tasks/Details/{ID}?modal={(Modal ? "true" : "false")}";
        var run = OverrideRunID ?? RunID;
        return run is null ? url : $"{url}&run={run}";
    }

    public async Task<IActionResult> OnGetAsync(int ID, bool Modal, [FromQuery(Name = "run")] long? RunID, CancellationToken CT)
    {
        this.ID = ID;
        this.Modal = Modal;
        this.RunID = RunID;
        var details = await TasksRepository.GetDetailsViewAsync(
            ID: ID,
            RunID: RunID,
            CT: CT);
        if (details is null) return NotFound();
        Details = details;
        if (Modal) ViewData["Modal"] = true;
        return Page();
    }

    /// <summary>Muestra un adjunto. El HTML siempre se descarga y con CSP sandbox para que nunca se ejecute en este dominio.</summary>
    public async Task<IActionResult> OnGetAttachmentAsync(int ID, long AttachmentID, CancellationToken CT)
    {
        this.ID = ID;
        var attachment = await AttachmentsRepository.GetFileAsync(
            TaskID: ID,
            AttachmentID: AttachmentID,
            CT: CT);
        if (attachment?.Content is null) return NotFound();
        var data = attachment.Content.Data;

        Response.Headers.XContentTypeOptions = "nosniff";
        if (attachment.Kind != AttachmentKind.PDF)
            Response.Headers.ContentSecurityPolicy = "sandbox; default-src 'none'; img-src 'self'; style-src 'unsafe-inline'";
        if (attachment.Kind == AttachmentKind.HTML)
            return new FileContentResult(data, "application/octet-stream") { FileDownloadName = attachment.FileName };
        return new FileContentResult(data, attachment.ContentType) { EnableRangeProcessing = true };
    }

    public Task<IActionResult> OnPostApproveAsync(int ID, bool Modal, CancellationToken CT) =>
        ActAsync(
            ID: ID,
            Modal: Modal,
            Action: item => WorkflowService.ApproveAsync(
                Item: item,
                Actor: Actor,
                CT: CT),
            Notice: "Aprobado.",
            CT: CT);

    public Task<IActionResult> OnPostRequestChangesAsync(int ID, bool Modal, string Feedback, CancellationToken CT) =>
        ActAsync(
            ID: ID,
            Modal: Modal,
            Action: item => WorkflowService.RequestChangesAsync(
                Item: item,
                Actor: Actor,
                Feedback: Feedback,
                CT: CT),
            Notice: "Cambios solicitados.",
            CT: CT);

    public Task<IActionResult> OnPostCommentAsync(int ID, bool Modal, string Body, bool AsFeedback, CancellationToken CT) =>
        ActAsync(
            ID: ID,
            Modal: Modal,
            Action: item => WorkflowService.AddCommentAsync(
                Item: item,
                Actor: Actor,
                Body: Body,
                AsFeedback: AsFeedback,
                CT: CT),
            Notice: "Comentario agregado.",
            CT: CT);

    public Task<IActionResult> OnPostRetryAsync(int ID, bool Modal, CancellationToken CT) =>
        ActAsync(
            ID: ID,
            Modal: Modal,
            Action: item => WorkflowService.RetryAsync(
                Item: item,
                Actor: Actor,
                CT: CT),
            Notice: "Ejecución encolada de nuevo.",
            CT: CT);

    public Task<IActionResult> OnPostCancelAsync(int ID, bool Modal, CancellationToken CT) =>
        ActAsync(
            ID: ID,
            Modal: Modal,
            Action: item => WorkflowService.CancelAsync(
                Item: item,
                Actor: Actor,
                CT: CT),
            Notice: "Cancelación solicitada.",
            CT: CT);

    public Task<IActionResult> OnPostResolveConflictsAsync(int ID, bool Modal, CancellationToken CT) =>
        ActAsync(
            ID: ID,
            Modal: Modal,
            Action: item => WorkflowService.ResolveConflictsAsync(
                Item: item,
                Actor: Actor,
                CT: CT),
            Notice: "La tarea regresó al desarrollador para arreglar los conflictos.",
            CT: CT);

    public Task<IActionResult> OnPostMoveAsync(int ID, bool Modal, int StageID, CancellationToken CT) =>
        ActAsync(
            ID: ID,
            Modal: Modal,
            Action: item => WorkflowService.MoveAsync(
                Item: item,
                StageID: StageID,
                Actor: Actor,
                CT: CT),
            Notice: "Tarea movida.",
            CT: CT);

    public Task<IActionResult> OnPostArchiveAsync(int ID, bool Modal, bool Archived, CancellationToken CT) =>
        ActAsync(
            ID: ID,
            Modal: Modal,
            Action: item => WorkflowService.SetArchivedAsync(
                Item: item,
                Archived: Archived,
                Actor: Actor,
                CT: CT),
            Notice: Archived ? "Tarea archivada." : "Tarea restaurada.",
            CT: CT);

    /// <summary>Elimina la tarea. Si se pudo, vuelve al tablero (sin abrir la tarea); si no, muestra el error en la tarea.</summary>
    public async Task<IActionResult> OnPostDeleteAsync(int ID, bool Modal, CancellationToken CT)
    {
        this.ID = ID;
        this.Modal = Modal;
        var task = await WorkflowService.LoadTaskAsync(
            TaskID: ID,
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
            return Redirect(Modal ? $"/{BoardQuery()}" : $"/Tasks/Details/{ID}");
        }
        return Redirect(Modal ? $"/{BoardQuery(OpenTask: false)}" : "/");
    }

    public Task<IActionResult> OnPostUpdateAsync(int ID, bool Modal, TaskUpdateViewModel Input, CancellationToken CT) =>
        ActAsync(
            ID: ID,
            Modal: Modal,
            Action: item => TasksRepository.UpdateAsync(
                Item: item,
                Record: Input,
                Actor: Actor,
                CT: CT),
            Notice: "Cambios guardados.",
            CT: CT);

    public Task<IActionResult> OnPostUploadAsync(int ID, bool Modal, List<IFormFile> Files, CancellationToken CT) =>
        ActAsync(
            ID: ID,
            Modal: Modal,
            Action: item => AttachmentsRepository.AddAsync(
                TaskID: item.ID,
                Files: Files,
                Actor: Actor,
                CT: CT),
            Notice: "Archivos adjuntados.",
            CT: CT);

    public Task<IActionResult> OnPostUploadCaptureAsync(int ID, bool Modal, List<IFormFile> Files, CancellationToken CT) =>
        ActAsync(
            ID: ID,
            Modal: Modal,
            Action: item => AttachmentsRepository.AddCapturesAsync(
                TaskID: item.ID,
                Files: Files,
                Actor: Actor,
                CT: CT),
            Notice: "Capturas agregadas.",
            CT: CT);

    public Task<IActionResult> OnPostDeleteAttachmentAsync(int ID, bool Modal, long AttachmentID, CancellationToken CT) =>
        ActAsync(
            ID: ID,
            Modal: Modal,
            Action: item => AttachmentsRepository.DeleteAsync(
                TaskID: item.ID,
                AttachmentID: AttachmentID,
                Actor: Actor,
                CT: CT),
            Notice: "Adjunto eliminado.",
            CT: CT);

    private string Actor => User.Identity?.Name ?? "?";

    private async Task<IActionResult> ActAsync(int ID, bool Modal, Func<TaskItem, Task> Action, string Notice, CancellationToken CT)
    {
        this.ID = ID;
        this.Modal = Modal;

        // Un valor que el formulario no pudo convertir (p. ej. una prioridad inexistente) llega como el valor por
        // defecto del tipo: si se ejecutara la acción se sobrescribiría el dato guardado sin avisar.
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Alguno de los datos enviados no es válido.";
            return Redirect(Modal ? $"/{BoardQuery()}" : $"/Tasks/Details/{ID}");
        }

        var task = await WorkflowService.LoadTaskAsync(
            TaskID: ID,
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
        return Redirect(Modal ? $"/{BoardQuery()}" : $"/Tasks/Details/{ID}");
    }

    private string BoardQuery(bool OpenTask = true)
    {
        var returnQuery = Request.Form["returnQuery"].ToString();
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(returnQuery);
        var keep = query
            .Where(kv => kv.Key is "project" or "worker" or "waiting" or "compact" or "hideEmpty" or "archived" or "q")
            .ToDictionary(kv => kv.Key, kv => (string?)kv.Value.ToString());
        if (OpenTask) keep["task"] = ID.ToString();
        return Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString("", keep);
    }

    public string HandlerUrl(string Handler) => $"/Tasks/Details/{ID}?handler={Handler}&modal={(Modal ? "true" : "false")}";

    public string AttachmentUrl(long AttachmentID) => $"/Tasks/Details/{ID}?handler=Attachment&attachmentId={AttachmentID}";

    public string StageName(int? ID) => Stages.FirstOrDefault(s => s.ID == ID)?.Name ?? "—";
}
