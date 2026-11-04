namespace DevArv.Contracts.Models;

/// <summary>Marcadores que el worker reemplaza en el prompt antes de ejecutar Claude.</summary>
public static class PromptTokens
{
    /// <summary>Ruta absoluta del directorio local con los adjuntos de la tarea.</summary>
    public const string AttachmentsDir = "{{attachmentsDir}}";
}
