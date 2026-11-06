using DevArv.Web.Enums.Tasks;

namespace DevArv.Web.DataModels;

public class TaskAttachment
{
    public long Id { get; set; }
    public int TaskId { get; set; }
    public TaskItem Task { get; set; } = null!;
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long Size { get; set; }
    public AttachmentKind Kind { get; set; }
    /// <summary>Bytes del archivo, guardados en la base de datos. Sólo se carga con Include; null si no se cargó.</summary>
    public TaskAttachmentContent? Content { get; set; }
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;
}
