using DevArv.Web.DataModels;
using DevArv.Web.DatabaseConnection;
using DevArv.Web.Enums.Runs;
using DevArv.Web.Enums.Tasks;
using DevArv.Web.Models;
using DevArv.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace DevArv.Web.Repositories;

/// <summary>Adjuntos y capturas de las tareas. El contenido se guarda en la base de datos, nunca en disco.</summary>
public class TaskAttachmentsRepository(AppDbContext Db, AttachmentStorage Storage)
{
    public AttachmentOptions Limits => Storage.Options;

    /// <summary>Adjunto con su contenido cargado; null si no pertenece a la tarea o no tiene contenido guardado.</summary>
    public Task<TaskAttachment?> GetFileAsync(int TaskID, long AttachmentID, CancellationToken CT) =>
        Db.TaskAttachments.AsNoTracking()
            .Include(a => a.Content)
            .FirstOrDefaultAsync(a => a.ID == AttachmentID && a.TaskID == TaskID && a.Content != null, CT);

    /// <summary>
    /// Adjunto de la tarea de una ejecución en curso de esa PC, con su contenido; null si la ejecución no es suya, ya no
    /// está en curso, el adjunto no es de su tarea (o es una captura, que no se envía al agente) o no tiene contenido.
    /// </summary>
    public Task<TaskAttachment?> GetFileForWorkerRunAsync(long RunID, long AttachmentID, int WorkerID, CancellationToken CT) =>
        Db.TaskRuns.AsNoTracking()
            .Where(r => r.ID == RunID && r.WorkerID == WorkerID && r.Status == RunStatus.RUNNING)
            .SelectMany(r => r.Task.Attachments)
            .Include(a => a.Content)
            .FirstOrDefaultAsync(a => a.ID == AttachmentID && !a.IsCapture && a.Content != null, CT);

    /// <summary>Valida y adjunta los archivos a la tarea. Lanza ValidationFailedException si no hay archivos o alguno es inválido.</summary>
    public async Task AddAsync(int TaskID, IReadOnlyCollection<IFormFile> Files, string Actor, CancellationToken CT)
    {
        if (Files.Count == 0)
            throw new ValidationFailedException(nameof(Files), "Selecciona al menos un archivo.");

        var existing = await Db.TaskAttachments.CountAsync(a => a.TaskID == TaskID, CT);
        try
        {
            Db.TaskAttachments.AddRange(await Storage.CreateAsync(
                TaskID: TaskID,
                Files: Files,
                ExistingCount: existing,
                CT: CT));
        }
        catch (AttachmentValidationException ex)
        {
            throw new ValidationFailedException(nameof(Files), ex.Message);
        }

        Db.TaskEvents.Add(new TaskEvent { TaskID = TaskID, Actor = Actor, Message = $"{Actor} adjuntó {Files.Count} archivo(s)" });
        await Db.SaveChangesAsync(CT);
    }

    /// <summary>
    /// Agrega capturas con los cambios aplicados (pestaña «Capturas»). Sólo se aceptan imágenes y cuentan para el
    /// máximo de archivos por tarea. No se envían al agente.
    /// </summary>
    public async Task AddCapturesAsync(int TaskID, IReadOnlyCollection<IFormFile> Files, string Actor, CancellationToken CT)
    {
        if (Files.Count == 0)
            throw new ValidationFailedException(nameof(Files), "Selecciona al menos una imagen.");

        var existing = await Db.TaskAttachments.CountAsync(a => a.TaskID == TaskID, CT);
        List<ValidatedUpload> validated;
        try
        {
            validated = await Storage.ValidateAsync(
                Files: Files,
                ExistingCount: existing,
                CT: CT);
        }
        catch (AttachmentValidationException ex)
        {
            throw new ValidationFailedException(nameof(Files), ex.Message);
        }
        if (validated.FirstOrDefault(v => v.Type.Kind != AttachmentKind.IMAGE) is { } notImage)
            throw new ValidationFailedException(nameof(Files),
                $"«{notImage.File.FileName}» no es una imagen. Las capturas deben ser PNG, JPG, GIF o WEBP.");

        var captures = await Storage.ReadAsync(
            TaskID: TaskID,
            Validated: validated,
            CT: CT);
        foreach (var capture in captures) capture.IsCapture = true;

        Db.TaskAttachments.AddRange(captures);
        Db.TaskEvents.Add(new TaskEvent { TaskID = TaskID, Actor = Actor, Message = $"{Actor} agregó {Files.Count} captura(s)" });
        await Db.SaveChangesAsync(CT);
    }

    public async Task DeleteAsync(int TaskID, long AttachmentID, string Actor, CancellationToken CT)
    {
        // Sin Include: el contenido está en la misma fila y se borra con ella, no hace falta leerlo.
        var attachment = await Db.TaskAttachments.FirstOrDefaultAsync(x => x.ID == AttachmentID && x.TaskID == TaskID, CT)
                         ?? throw new ValidationFailedException(nameof(AttachmentID), "El adjunto no existe.");
        Db.TaskAttachments.Remove(attachment);
        Db.TaskEvents.Add(new TaskEvent { TaskID = TaskID, Actor = Actor, Message = $"{Actor} eliminó el adjunto «{attachment.FileName}»" });
        await Db.SaveChangesAsync(CT);
    }
}
