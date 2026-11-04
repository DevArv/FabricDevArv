using System.Text.RegularExpressions;

namespace DevArv.Web.Helpers;

/// <summary>Interpreta el repositorio que escribe el usuario: "owner/repo", URL https o URL SSH de GitHub.</summary>
public static partial class GitHubRepositoryParser
{
    public static bool TryParse(string? input, out string owner, out string repo)
    {
        owner = repo = "";
        if (string.IsNullOrWhiteSpace(input)) return false;
        var m = RepoPattern().Match(input.Trim());
        if (!m.Success) return false;
        owner = m.Groups["owner"].Value;
        repo = m.Groups["repo"].Value;
        if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) repo = repo[..^4];
        return owner.Length > 0 && repo.Length > 0;
    }

    [GeneratedRegex(@"^(?:(?:https?://)?(?:www\.)?github\.com/|git@github\.com:)?(?<owner>[A-Za-z0-9](?:[A-Za-z0-9-]{0,38}))/(?<repo>[A-Za-z0-9._-]{1,100})/?$")]
    private static partial Regex RepoPattern();
}
