namespace DevArv.Web.Models;

/// <param name="HasConflicts">
/// true si GitHub informa conflictos con la rama base; null si no lo sabe (aún lo está calculando o la respuesta no
/// trae ese dato, como el listado de pull requests).
/// </param>
public sealed record PullRequestInfo(int Number, string HtmlUrl, string State, bool Merged, bool? HasConflicts = null);
