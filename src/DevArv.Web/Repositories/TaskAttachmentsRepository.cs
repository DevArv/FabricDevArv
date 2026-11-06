using DevArv.Web.DataModels;
using DevArv.Web.DatabaseConnection;
using DevArv.Web.Enums.Runs;
using DevArv.Web.Models;
using DevArv.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace DevArv.Web.Repositories;

public class TaskAttachmentsRepository(AppDbContext Db, AttachmentStorage Storage, ILogger<TaskAttachmentsRepository> Logger)
{
    public AttachmentOptions Limits => Storage.Options;

    /// <summary>Adjunto y ruta física en disco; null si el adjunto no pertenece a la tarea o el archivo no existe.</summary>
    public async Task<(TaskAttachment Attachment, string Path)?> GetFileAsync(int TaskID, long AttachmentID, CancellationToken CT)
    {
        var attachment = await Db.TaskAttachments.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == AttachmentID && x.TaskId == TaskID, CT);
        if (attachment is null) return null;

        var path = Storage.PathOf(attachment);
        return File.Exists(path) ? (attachment, path) : null;
    }

    /// <summary>
    /// Adjunto de la tarea de una ejecución en curso de esa PC; null si la ejecución no es suya, ya no está en curso,
    /// el adjunto no es de su tarea o el archivo no existe.
    /// </summary>
    public async Task<(TaskAttachment Attachment, string Path)?> GetFileForWorkerRunAsync(
        long RunID, long AttachmentID, int WorkerID, CancellationToken CT)
    {
        var attachment = await Db.TaskRuns
            .Where(r => r.Id == RunID && r.WorkerId == WorkerID && r.Status == RunStatus.Running)
            .SelectMany(r => r.Task.Attachments)
            .FirstOrDefaultAsync(a => a.Id == AttachmentID, CT);
        if (attachment is null) return null;

        var path = Storage.PathOf(attachment);
        return File.Exists(path) ? (attachment, path) : null;
    }

    /// <summary>
    /// Valida y adjunta los archivos a la tarea. Lanza ValidationFailedException si no hay archivos o alguno es inválido.
    /// Si no se puede guardar en la base de datos, borra los archivos que ya escribió en disco.
    /// </summary>
    public async Task AddAsync(int TaskID, IReadOnlyCollection<IFormFile> Files, string Actor, CancellationToken CT)
    {
        if (Files.Count == 0)
            throw new ValidationFailedException(nameof(Files), "Selecciona al menos un archivo.");

        var existing = await Db.TaskAttachments.CountAsync(a => a.TaskId == TaskID, CT);
        List<TaskAttachment> saved;
        try
        {
            saved = await Storage.SaveAsync(
                TaskID: TaskID,
                Files: Files,
                ExistingCount: existing,
                CT: CT);
        }
        catch (AttachmentValidationException ex)
        {
            throw new ValidationFailedException(nameof(Files), ex.Message);
        }

        try
        {
            Db.TaskAttachments.AddRange(saved);
            Db.TaskEvents.Add(new TaskEvent { TaskId = TaskID, Actor = Actor, Message = $"{Actor} adjuntó {Files.Count} archivo(s)" });
            await Db.SaveChangesAsync(CT);
        }
        catch
        {
            foreach (var attachment in saved) DeleteFromDisk(attachment);
            throw;
        }
    }

    public async Task DeleteAsync(int TaskID, long AttachmentID, string Actor, CancellationToken CT)
    {
        var attachment = await Db.TaskAttachments.FirstOrDefaultAsync(x => x.Id == AttachmentID && x.TaskId == TaskID, CT)
                         ?? throw new ValidationFailedException(nameof(AttachmentID), "El adjunto no existe.");
        Db.TaskAttachments.Remove(attachment);
        Db.TaskEvents.Add(new TaskEvent { TaskId = TaskID, Actor = Actor, Message = $"{Actor} eliminó el adjunto «{attachment.FileName}»" });
        await Db.SaveChangesAsync(CT);

        // El registro ya no existe: un archivo que no se pueda borrar no debe mostrarse al usuario como error.
        DeleteFromDisk(attachment);
    }

    private void DeleteFromDisk(TaskAttachment Attachment)
    {
        try
        {
            Storage.Delete(Attachment);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.LogWarning(ex, "No se pudo borrar del disco el adjunto {AttachmentId}.", Attachment.Id);
        }
    }
}
