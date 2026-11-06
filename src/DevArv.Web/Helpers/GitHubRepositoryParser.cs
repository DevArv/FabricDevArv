using System.Text.RegularExpressions;

namespace DevArv.Web.Helpers;

/// <summary>Interpreta el repositorio que escribe el usuario: "owner/repo", URL https o URL SSH de GitHub.</summary>
public static partial class GitHubRepositoryParser
{
    public static bool TryParse(string? Input, out string Owner, out string Repo)
    {
        Owner = Repo = "";
        if (string.IsNullOrWhiteSpace(Input)) return false;
        var m = RepoPattern().Match(Input.Trim());
        if (!m.Success) return false;
        Owner = m.Groups["owner"].Value;
        Repo = m.Groups["repo"].Value;
        if (Repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) Repo = Repo[..^4];
        return Owner.Length > 0 && Repo.Length > 0;
    }

    [GeneratedRegex(@"^(?:(?:https?://)?(?:www\.)?github\.com/|git@github\.com:)?(?<owner>[A-Za-z0-9](?:[A-Za-z0-9-]{0,38}))/(?<repo>[A-Za-z0-9._-]{1,100})/?$")]
    private static partial Regex RepoPattern();
}
