using DevArv.Web.DataModels;
using DevArv.Web.DatabaseConnection;
using DevArv.Web.Models;
using DevArv.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace DevArv.Web.Repositories;

public class TaskAttachmentsRepository(AppDbContext db, AttachmentStorage storage)
{
    public AttachmentOptions Limits => storage.Options;

    /// <summary>Adjunto y ruta física en disco; null si el adjunto no pertenece a la tarea o el archivo no existe.</summary>
    public async Task<(TaskAttachment Attachment, string Path)?> GetFileAsync(int taskId, long attachmentId, CancellationToken ct)
    {
        var attachment = await db.TaskAttachments.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == attachmentId && x.TaskId == taskId, ct);
        if (attachment is null) return null;

        var path = storage.PathOf(attachment);
        return File.Exists(path) ? (attachment, path) : null;
    }

    public async Task AddAsync(int taskId, IReadOnlyCollection<IFormFile> files, string actor, CancellationToken ct)
    {
        var existing = await db.TaskAttachments.CountAsync(a => a.TaskId == taskId, ct);
        try
        {
            db.TaskAttachments.AddRange(await storage.SaveAsync(taskId, files, existing, ct));
        }
        catch (AttachmentValidationException ex)
        {
            throw new ValidationFailedException(nameof(files), ex.Message);
        }
        db.TaskEvents.Add(new TaskEvent { TaskId = taskId, Actor = actor, Message = $"{actor} adjuntó {files.Count} archivo(s)" });
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int taskId, long attachmentId, string actor, CancellationToken ct)
    {
        var attachment = await db.TaskAttachments.FirstOrDefaultAsync(x => x.Id == attachmentId && x.TaskId == taskId, ct)
                         ?? throw new ValidationFailedException(nameof(attachmentId), "El adjunto no existe.");
        db.TaskAttachments.Remove(attachment);
        db.TaskEvents.Add(new TaskEvent { TaskId = taskId, Actor = actor, Message = $"{actor} eliminó el adjunto «{attachment.FileName}»" });
        await db.SaveChangesAsync(ct);
        storage.Delete(attachment);
    }
}
