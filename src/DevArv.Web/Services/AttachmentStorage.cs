using System.Text;
using DevArv.Web.DataModels;
using DevArv.Web.Helpers;
using DevArv.Web.Models;

namespace DevArv.Web.Services;

/// <summary>Guarda los adjuntos en disco con nombres aleatorios, fuera de wwwroot.</summary>
public sealed class AttachmentStorage
{
    private const int HEAD_BYTES = 4096;
    private readonly AttachmentOptions _Options;
    private readonly string _Root;

    public AttachmentStorage(Microsoft.Extensions.Options.IOptions<AttachmentOptions> OptionsAccessor, IWebHostEnvironment Environment)
    {
        _Options = OptionsAccessor.Value;
        _Root = Path.IsPathRooted(_Options.StoragePath)
            ? _Options.StoragePath
            : Path.Combine(Environment.ContentRootPath, _Options.StoragePath);
        Directory.CreateDirectory(_Root);
    }

    public AttachmentOptions Options => _Options;

    /// <summary>Valida y guarda. Lanza AttachmentValidationException sin guardar nada si algún archivo es inválido.</summary>
    public async Task<List<TaskAttachment>> SaveAsync(int TaskID, IReadOnlyCollection<IFormFile> Files, int ExistingCount, CancellationToken CT)
    {
        var validated = await ValidateAsync(
            Files: Files,
            ExistingCount: ExistingCount,
            CT: CT);
        return await StoreAsync(
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

    /// <summary>Escribe los archivos ya validados. Si alguna escritura falla borra los que ya había escrito.</summary>
    public async Task<List<TaskAttachment>> StoreAsync(int TaskID, IReadOnlyList<ValidatedUpload> Validated, CancellationToken CT)
    {
        var saved = new List<TaskAttachment>();
        try
        {
            foreach (var (file, type) in Validated)
            {
                var storedName = $"{Guid.NewGuid():N}{type.Extension}";
                await using (var target = File.Create(Path.Combine(_Root, storedName)))
                await using (var source = file.OpenReadStream())
                {
                    await source.CopyToAsync(target, CT);
                }

                saved.Add(new TaskAttachment
                {
                    TaskID = TaskID,
                    FileName = SanitizeFileName(file.FileName),
                    ContentType = type.ContentType,
                    Kind = type.Kind,
                    Size = file.Length,
                    StoredName = storedName,
                });
            }
        }
        catch
        {
            DeleteQuietly(saved);
            throw;
        }
        return saved;
    }

    public string PathOf(TaskAttachment Attachment)
    {
        // StoredName lo genera el servidor, pero se valida igual para evitar path traversal.
        var name = Path.GetFileName(Attachment.StoredName);
        return Path.Combine(_Root, name);
    }

    public void Delete(TaskAttachment Attachment)
    {
        var path = PathOf(Attachment);
        if (File.Exists(path)) File.Delete(path);
    }

    /// <summary>Borra los archivos de los adjuntos sin lanzar errores (limpieza tras un fallo).</summary>
    public void DeleteQuietly(IEnumerable<TaskAttachment> Attachments)
    {
        foreach (var attachment in Attachments)
        {
            try { Delete(attachment); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
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
