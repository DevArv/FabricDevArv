using System.Globalization;
using System.Text.RegularExpressions;
using System.Text;

namespace DevArv.Web.Helpers;

public static partial class Slug
{
    /// <summary>"Requerimientos de mejora para Fabric" → "requerimientos-de-mejora-para-fabric".</summary>
    public static string Create(string text, int maxLength = 50)
    {
        if (string.IsNullOrWhiteSpace(text)) return "tarea";
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.ToLowerInvariant(c));
        }
        var slug = NonAlnum().Replace(sb.ToString(), "-").Trim('-');
        if (slug.Length > maxLength) slug = slug[..maxLength].TrimEnd('-');
        return slug.Length == 0 ? "tarea" : slug;
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlnum();
}
