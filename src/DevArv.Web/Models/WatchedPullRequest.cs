namespace DevArv.Web.Models;

/// <summary>Pull request abierto de una tarea que el orquestador revisa para detectar su merge.</summary>
public sealed record WatchedPullRequest(int TaskId, string RepoOwner, string RepoName, int Number);
