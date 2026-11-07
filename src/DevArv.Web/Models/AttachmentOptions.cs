namespace DevArv.Web.Models;

/// <summary>Límites de los adjuntos. El contenido se guarda en la base de datos (columna Tasks_Attachments.Content).</summary>
public sealed class AttachmentOptions
{
    /// <summary>
    /// Sólo para migrar: carpeta (relativa a la raíz del proyecto web, o absoluta) donde se guardaban los adjuntos antes de
    /// pasar a la base de datos. <c>DbInitializer</c> la usa para copiarlos y ya no se escribe nada en ella.
    /// Quitar junto con la migración cuando todas las bases la hayan hecho.
    /// </summary>
    public string StoragePath { get; set; } = "App_Data/attachments";
    public int MaxFilesPerTask { get; set; } = 15;
    public long MaxFileBytes { get; set; } = 20 * 1024 * 1024;
}
