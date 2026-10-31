using System.Text;
using DevArv.Web.Domain;

namespace DevArv.Web.Services;

public sealed class AttachmentOptions
{
    public string StoragePath { get; set; } = "App_Data/attachments";
    public int MaxFilesPerTask { get; set; } = 15;
    public long MaxFileBytes { get; set; } = 20 * 1024 * 1024;
}

public sealed record DetectedFile(AttachmentKind Kind, string ContentType, string Extension);

/// <summary>
/// Valida el tipo real de un archivo por su contenido (firma / "magic bytes"), no por su extensión.
/// Sólo se aceptan imágenes (PNG, JPEG, GIF, WEBP), PDF y HTML, y la extensión debe coincidir con el contenido.
/// </summary>
public static class FileSignature
{
    private static readonly Dictionary<string, string[]> ExtensionsByType = new()
    {
        ["image/png"] = [".png"],
        ["image/jpeg"] = [".jpg", ".jpeg"],
        ["image/gif"] = [".gif"],
        ["image/webp"] = [".webp"],
        ["application/pdf"] = [".pdf"],
        ["text/html"] = [".html", ".htm"],
    };

    public static readonly string AcceptAttribute = ".png,.jpg,.jpeg,.gif,.webp,.pdf,.html,.htm";

    public static DetectedFile? Detect(ReadOnlySpan<byte> head, string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var detected = DetectByContent(head);
        if (detected is null) return null;
        // La extensión declarada debe corresponder al contenido real.
        return ExtensionsByType.TryGetValue(detected.ContentType, out var exts) && exts.Contains(ext)
            ? detected with { Extension = ext }
            : null;
    }

    private static DetectedFile? DetectByContent(ReadOnlySpan<byte> h)
    {
        if (StartsWith(h, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
            return new(AttachmentKind.Image, "image/png", ".png");
        if (StartsWith(h, [0xFF, 0xD8, 0xFF]))
            return new(AttachmentKind.Image, "image/jpeg", ".jpg");
        if (StartsWith(h, "GIF87a"u8) || StartsWith(h, "GIF89a"u8))
            return new(AttachmentKind.Image, "image/gif", ".gif");
        if (h.Length >= 12 && StartsWith(h, "RIFF"u8) && h.Slice(8, 4).SequenceEqual("WEBP"u8))
            return new(AttachmentKind.Image, "image/webp", ".webp");
        if (StartsWith(h, "%PDF-"u8))
            return new(AttachmentKind.Pdf, "application/pdf", ".pdf");
        if (LooksLikeHtml(h))
            return new(AttachmentKind.Html, "text/html", ".html");
        return null;
    }

    /// <summary>HTML no tiene firma binaria: se exige texto UTF-8 válido, sin bytes nulos, que empiece como documento HTML.</summary>
    private static bool LooksLikeHtml(ReadOnlySpan<byte> h)
    {
        if (h.IsEmpty || h.Contains((byte)0)) return false;
        if (StartsWith(h, [0xEF, 0xBB, 0xBF])) h = h[3..];
        string text;
        try
        {
            // El encabezado puede cortar un carácter multibyte al final; se toleran hasta 3 bytes truncados.
            var decoder = new UTF8Encoding(false, throwOnInvalidBytes: true);
            var safe = h.Length > 3 ? h[..TrimIncompleteUtf8(h)] : h;
            text = decoder.GetString(safe);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        var start = text.TrimStart().ToLowerInvariant();
        if (start.StartsWith("<!--"))
        {
            var end = start.IndexOf("-->", StringComparison.Ordinal);
            if (end < 0) return false;
            start = start[(end + 3)..].TrimStart();
        }
        return start.StartsWith("<!doctype html") || start.StartsWith("<html");
    }

    private static int TrimIncompleteUtf8(ReadOnlySpan<byte> h)
    {
        var len = h.Length;
        for (var i = 1; i <= 3 && i <= len; i++)
        {
            var b = h[len - i];
            if ((b & 0xC0) == 0x80) continue; // byte de continuación
            if ((b & 0x80) == 0) return len; // ASCII: completo
            var needed = (b & 0xE0) == 0xC0 ? 2 : (b & 0xF0) == 0xE0 ? 3 : (b & 0xF8) == 0xF0 ? 4 : 1;
            return needed > i ? len - i : len;
        }
        return len;
    }

    private static bool StartsWith(ReadOnlySpan<byte> data, ReadOnlySpan<byte> prefix) =>
        data.Length >= prefix.Length && data[..prefix.Length].SequenceEqual(prefix);
}

public sealed class AttachmentValidationException(string message) : Exception(message);

public sealed record ValidatedUpload(IFormFile File, DetectedFile Type);

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
