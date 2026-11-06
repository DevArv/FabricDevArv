namespace DevArv.Web.DataModels;

/// <summary>
/// Contenido binario de un <see cref="TaskAttachment"/>. Comparte la tabla <c>TaskAttachments</c> (columna <c>Content</c>)
/// pero es otra entidad para que listar adjuntos no cargue los bytes: sólo se lee al descargar el archivo.
/// </summary>
public class TaskAttachmentContent
{
    /// <summary>Mismo Id que el <see cref="TaskAttachment"/>.</summary>
    public long Id { get; set; }
    public byte[] Data { get; set; } = [];
}
