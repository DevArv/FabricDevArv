using DevArv.Web.DataModels;
using DevArv.Web.DatabaseConnection;
using DevArv.Web.Models;
using DevArv.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace DevArv.Web.Repositories;

public class TaskAttachmentsRepository(AppDbContext db, AttachmentStorage storage)
{
    public AttachmentOptions Limits => storage.Options;

    /// <summary>Adjunto con su contenido cargado; null si no pertenece a la tarea o no tiene contenido guardado.</summary>
    public Task<TaskAttachment?> GetFileAsync(int taskId, long attachmentId, CancellationToken ct) =>
        db.TaskAttachments.AsNoTracking()
            .Include(a => a.Content)
            .FirstOrDefaultAsync(a => a.Id == attachmentId && a.TaskId == taskId && a.Content != null, ct);

    public async Task AddAsync(int taskId, IReadOnlyCollection<IFormFile> files, string actor, CancellationToken ct)
    {
        var existing = await db.TaskAttachments.CountAsync(a => a.TaskId == taskId, ct);
        try
        {
            db.TaskAttachments.AddRange(await storage.CreateAsync(taskId, files, existing, ct));
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
        // Sin Include: el contenido está en la misma fila y se borra con ella, no hace falta leerlo.
        var attachment = await db.TaskAttachments.FirstOrDefaultAsync(x => x.Id == attachmentId && x.TaskId == taskId, ct)
                         ?? throw new ValidationFailedException(nameof(attachmentId), "El adjunto no existe.");
        db.TaskAttachments.Remove(attachment);
        db.TaskEvents.Add(new TaskEvent { TaskId = taskId, Actor = actor, Message = $"{actor} eliminó el adjunto «{attachment.FileName}»" });
        await db.SaveChangesAsync(ct);
    }
}
