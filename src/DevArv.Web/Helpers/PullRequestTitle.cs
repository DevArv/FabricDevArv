using System.Text.RegularExpressions;

namespace DevArv.Web.Helpers;

/// <summary>Estándar de nombrado de los pull requests que crea DevArv.</summary>
public static partial class PullRequestTitle
{
    /// <summary>
    /// El título del PR es el nombre de la rama con cada grupo de caracteres no alfanuméricos
    /// (<c>/</c>, <c>_</c>, <c>-</c>, espacios…) reemplazado por un solo guion: "fd_/1-prueba-pr" → "fd-1-prueba-pr".
    /// </summary>
    public static string FromBranch(string Branch)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Branch);
        var title = Separators().Replace(Branch.Trim(), "-").Trim('-');
        return title.Length == 0 ? Branch.Trim() : title;
    }

    [GeneratedRegex("[^A-Za-z0-9]+")]
    private static partial Regex Separators();
}
