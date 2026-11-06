using System.Text;
using DevArv.Web.DataModels;
using DevArv.Web.Helpers;
using DevArv.Web.Models;
using Microsoft.Extensions.Options;

namespace DevArv.Web.Services;

/// <summary>
/// Valida los adjuntos y arma las entidades con su contenido, que se guarda en la base de datos
/// (columna TaskAttachments.Content). No escribe nada en disco.
/// </summary>
public sealed class AttachmentStorage(IOptions<AttachmentOptions> options)
{
    private const int HeadBytes = 4096;
    private readonly AttachmentOptions _options = options.Value;

    public AttachmentOptions Options => _options;

    /// <summary>Valida y arma los adjuntos. Lanza AttachmentValidationException sin devolver nada si algún archivo es inválido.</summary>
    public async Task<List<TaskAttachment>> CreateAsync(int taskId, IReadOnlyCollection<IFormFile> files, int existingCount, CancellationToken ct)
    {
        var validated = await ValidateAsync(files, existingCount, ct);
        return await ReadAsync(taskId, validated, ct);
    }

    /// <summary>Valida cantidad, tamaño y tipo real de todos los archivos. Lanza AttachmentValidationException.</summary>
    public async Task<List<ValidatedUpload>> ValidateAsync(IReadOnlyCollection<IFormFile> files, int existingCount, CancellationToken ct)
    {
        if (files.Count == 0) return [];
        if (existingCount + files.Count > _options.MaxFilesPerTask)
            throw new AttachmentValidationException(
                $"Máximo {_options.MaxFilesPerTask} archivos por tarea (ya hay {existingCount}, intentas agregar {files.Count}).");

        var validated = new List<ValidatedUpload>();
        foreach (var file in files)
        {
            if (file.Length == 0)
                throw new AttachmentValidationException($"«{file.FileName}» está vacío.");
            if (file.Length > _options.MaxFileBytes)
                throw new AttachmentValidationException(
                    $"«{file.FileName}» supera el máximo de {_options.MaxFileBytes / (1024 * 1024)} MB.");

            var head = new byte[(int)Math.Min(HeadBytes, file.Length)];
            await using (var s = file.OpenReadStream())
            {
                await s.ReadExactlyAsync(head, ct);
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
    public async Task<List<TaskAttachment>> ReadAsync(int taskId, IReadOnlyList<ValidatedUpload> validated, CancellationToken ct)
    {
        var attachments = new List<TaskAttachment>();
        foreach (var (file, type) in validated)
        {
            var data = new byte[file.Length];
            await using (var source = file.OpenReadStream())
            {
                await source.ReadExactlyAsync(data, ct);
            }

            attachments.Add(new TaskAttachment
            {
                TaskId = taskId,
                FileName = SanitizeFileName(file.FileName),
                ContentType = type.ContentType,
                Kind = type.Kind,
                Size = data.LongLength,
                Content = new TaskAttachmentContent { Data = data },
            });
        }
        return attachments;
    }

    public static string SanitizeFileName(string fileName)
    {
        // Se quitan rutas y caracteres inválidos en Windows o Linux: el worker guarda el archivo con este nombre.
        var name = fileName.Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
            sb.Append(char.IsControl(c) || "<>:\"|?*".Contains(c) ? '_' : c);
        name = sb.ToString().Trim().TrimEnd('.');
        if (name.Length == 0 || name.Trim('.').Length == 0) return "archivo";
        return name.Length > 120 ? name[^120..] : name;
    }
}
