using System.Text;
using DevArv.Web.DataModels;
using DevArv.Web.Helpers;
using DevArv.Web.Models;
using Microsoft.Extensions.Options;

namespace DevArv.Web.Services;

/// <summary>
/// Valida los adjuntos y arma las entidades con su contenido, que se guarda en la base de datos
/// (columna Tasks_Attachments.Content). No escribe nada en disco.
/// </summary>
public sealed class AttachmentStorage(IOptions<AttachmentOptions> OptionsAccessor)
{
    private const int HEAD_BYTES = 4096;
    private readonly AttachmentOptions _Options = OptionsAccessor.Value;

    public AttachmentOptions Options => _Options;

    /// <summary>Valida y arma los adjuntos. Lanza AttachmentValidationException sin devolver nada si algún archivo es inválido.</summary>
    public async Task<List<TaskAttachment>> CreateAsync(int TaskID, IReadOnlyCollection<IFormFile> Files, int ExistingCount, CancellationToken CT)
    {
        var validated = await ValidateAsync(
            Files: Files,
            ExistingCount: ExistingCount,
            CT: CT);
        return await ReadAsync(
            TaskID: TaskID,
            Validated: validated,
            CT: CT);
    }

    /// <summary>Valida cantidad, tamaño y tipo real de todos los archivos. Lanza AttachmentValidationException.</summary>
    public async Task<List<ValidatedUpload>> ValidateAsync(IReadOnlyCollection<IFormFile> Files, int ExistingCount, CancellationToken CT)
    {
        if (Files.Count == 0) return [];
        if (ExistingCount + Files.Count > _Options.MaxFilesPerTask)
            throw new AttachmentValidationException(
                $"Máximo {_Options.MaxFilesPerTask} archivos por tarea (ya hay {ExistingCount}, intentas agregar {Files.Count}).");

        var validated = new List<ValidatedUpload>();
        foreach (var file in Files)
        {
            if (file.Length == 0)
                throw new AttachmentValidationException($"«{file.FileName}» está vacío.");
            if (file.Length > _Options.MaxFileBytes)
                throw new AttachmentValidationException(
                    $"«{file.FileName}» supera el máximo de {_Options.MaxFileBytes / (1024 * 1024)} MB.");

            var head = new byte[(int)Math.Min(HEAD_BYTES, file.Length)];
            await using (var source = file.OpenReadStream())
            {
                await source.ReadExactlyAsync(head, CT);
            }

            var type = FileSignature.Detect(head, file.FileName)
                ?? throw new AttachmentValidationException(
                    $"«{file.FileName}» no es un formato permitido. Sólo se aceptan imágenes (PNG, JPG, GIF, WEBP), PDF y HTML, " +
                    "y la extensión debe coincidir con el contenido real del archivo.");
            validated.Add(new ValidatedUpload(file, type));
        }
        return validated;
    }

    /// <summary>Lee el contenido completo de los archivos ya validados y arma las entidades (no las agrega al contexto).</summary>
    public async Task<List<TaskAttachment>> ReadAsync(int TaskID, IReadOnlyList<ValidatedUpload> Validated, CancellationToken CT)
    {
        var attachments = new List<TaskAttachment>();
        foreach (var (file, type) in Validated)
        {
            var data = new byte[file.Length];
            await using (var source = file.OpenReadStream())
            {
                await source.ReadExactlyAsync(data, CT);
            }

            attachments.Add(new TaskAttachment
            {
                TaskID = TaskID,
                FileName = SanitizeFileName(file.FileName),
                ContentType = type.ContentType,
                Kind = type.Kind,
                Size = data.LongLength,
                Content = new TaskAttachmentContent { Data = data },
            });
        }
        return attachments;
    }

    public static string SanitizeFileName(string FileName)
    {
        // Se quitan rutas y caracteres inválidos en Windows o Linux: el worker guarda el archivo con este nombre.
        var name = FileName.Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        var builder = new StringBuilder(name.Length);
        foreach (var c in name)
            builder.Append(char.IsControl(c) || "<>:\"|?*".Contains(c) ? '_' : c);
        name = builder.ToString().Trim().TrimEnd('.');
        if (name.Length == 0 || name.Trim('.').Length == 0) return "archivo";
        return name.Length > 120 ? name[^120..] : name;
    }
}
