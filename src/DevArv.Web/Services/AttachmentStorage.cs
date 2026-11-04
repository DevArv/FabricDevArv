using System.Text;
using DevArv.Web.DataModels;
using DevArv.Web.Helpers;
using DevArv.Web.Models;

namespace DevArv.Web.Services;

/// <summary>Guarda los adjuntos en disco con nombres aleatorios, fuera de wwwroot.</summary>
public sealed class AttachmentStorage
{
    private const int HeadBytes = 4096;
    private readonly AttachmentOptions _options;
    private readonly string _root;

    public AttachmentStorage(Microsoft.Extensions.Options.IOptions<AttachmentOptions> options, IWebHostEnvironment env)
    {
        _options = options.Value;
        _root = Path.IsPathRooted(_options.StoragePath)
            ? _options.StoragePath
            : Path.Combine(env.ContentRootPath, _options.StoragePath);
        Directory.CreateDirectory(_root);
    }

    public AttachmentOptions Options => _options;

    /// <summary>Valida y guarda. Lanza AttachmentValidationException sin guardar nada si algún archivo es inválido.</summary>
    public async Task<List<TaskAttachment>> SaveAsync(int taskId, IReadOnlyCollection<IFormFile> files, int existingCount, CancellationToken ct)
    {
        var validated = await ValidateAsync(files, existingCount, ct);
        return await StoreAsync(taskId, validated, ct);
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

    public async Task<List<TaskAttachment>> StoreAsync(int taskId, IReadOnlyList<ValidatedUpload> validated, CancellationToken ct)
    {
        var saved = new List<TaskAttachment>();
        foreach (var (file, type) in validated)
        {
            var storedName = $"{Guid.NewGuid():N}{type.Extension}";
            await using (var target = File.Create(Path.Combine(_root, storedName)))
            await using (var source = file.OpenReadStream())
            {
                await source.CopyToAsync(target, ct);
            }

            saved.Add(new TaskAttachment
            {
                TaskId = taskId,
                FileName = SanitizeFileName(file.FileName),
                ContentType = type.ContentType,
                Kind = type.Kind,
                Size = file.Length,
                StoredName = storedName,
            });
        }
        return saved;
    }

    public string PathOf(TaskAttachment attachment)
    {
        // StoredName lo genera el servidor, pero se valida igual para evitar path traversal.
        var name = Path.GetFileName(attachment.StoredName);
        return Path.Combine(_root, name);
    }

    public void Delete(TaskAttachment attachment)
    {
        var path = PathOf(attachment);
        if (File.Exists(path)) File.Delete(path);
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
