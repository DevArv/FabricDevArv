namespace DevArv.Web.Models;

public sealed record RepoDetails(string FullName, string DefaultBranch, bool IsPrivate, string HtmlUrl);
