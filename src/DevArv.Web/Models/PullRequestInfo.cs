namespace DevArv.Web.Models;

public sealed record PullRequestInfo(int Number, string HtmlUrl, string State, bool Merged);
