namespace DevArv.Web.Models;

/// <summary>
/// Pull request abierto de una tarea que el orquestador revisa para detectar su merge o sus conflictos.
/// <paramref name="HasConflicts"/> es lo último que se registró en la tarea.
/// </summary>
public sealed record WatchedPullRequest(int TaskId, string RepoOwner, string RepoName, int Number, bool HasConflicts = false);
