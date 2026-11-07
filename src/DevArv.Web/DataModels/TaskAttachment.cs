using DevArv.Web.Enums.Tasks;

namespace DevArv.Web.DataModels;

public class TaskAttachment
{
    public long ID { get; set; }
    public int TaskID { get; set; }
    public TaskItem Task { get; set; } = null!;
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long Size { get; set; }
    public AttachmentKind Kind { get; set; }
    /// <summary>Bytes del archivo, guardados en la base de datos. Sólo se carga con Include; null si no se cargó.</summary>
    public TaskAttachmentContent? Content { get; set; }
    /// <summary>
    /// Captura con los cambios aplicados (pestaña «Capturas» del detalle), no un adjunto de la definición de la tarea:
    /// no se envía al agente.
    /// </summary>
    public bool IsCapture { get; set; }
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;
}
