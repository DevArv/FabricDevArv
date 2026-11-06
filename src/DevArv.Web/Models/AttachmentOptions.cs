namespace DevArv.Web.Models;

/// <summary>Límites de los adjuntos. El contenido se guarda en la base de datos (columna TaskAttachments.Content).</summary>
public sealed class AttachmentOptions
{
    public int MaxFilesPerTask { get; set; } = 15;
    public long MaxFileBytes { get; set; } = 20 * 1024 * 1024;
}
