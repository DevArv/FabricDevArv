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
    /// <summary>Nombre del archivo en disco (GUID + extensión). Nunca se usa el nombre del usuario.</summary>
    public string StoredName { get; set; } = "";
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;
}
