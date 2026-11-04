using System.Text;
using DevArv.Web.Enums.Tasks;
using DevArv.Web.Models;

namespace DevArv.Web.Helpers;

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
