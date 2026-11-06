using DevArv.Web.DataModels;
using DevArv.Web.Enums.Tasks;
using DevArv.Web.Enums.Workflow;

namespace DevArv.Web.Helpers;

/// <summary>Cuándo el pull request de una tarea tiene conflictos, cuándo se vigila y cuándo se puede pedir arreglarlos.</summary>
public static class PullRequestConflictRule
{
    /// <summary>
    /// Interpreta los campos <c>mergeable</c> y <c>mergeable_state</c> de la API de GitHub. <c>mergeable_state = dirty</c>
    /// o <c>mergeable = false</c> indican conflictos; <c>mergeable = null</c> significa que GitHub aún lo está calculando
    /// (devuelve null: no se sabe).
    /// </summary>
    public static bool? FromGitHub(bool? mergeable, string? mergeableState)
    {
        if (string.Equals(mergeableState, "dirty", StringComparison.OrdinalIgnoreCase)) return true;
        return mergeable is null ? null : !mergeable.Value;
    }

    /// <summary>
    /// Si se revisa en GitHub el pull request de una tarea en <paramref name="current"/>: sin columna destino del merge,
    /// toda columna no final; con ella, sólo las anteriores (las que <see cref="MergedPullRequestRule"/> movería).
    /// </summary>
    public static bool ShouldWatch(Stage current, Stage? mergeDestination) =>
        mergeDestination is null
            ? current.Kind != StageKind.Terminal
            : MergedPullRequestRule.ShouldMove(current, mergeDestination);

    /// <summary>
    /// Se puede pedir al desarrollador que arregle los conflictos: el pull request sigue sin integrarse, GitHub informó
    /// conflictos, la tarea no está archivada ni en una columna final y no hay una ejecución en cola o en curso.
    /// </summary>
    public static bool CanResolve(TaskItem task, Stage current) =>
        ShowConflicts(task)
        && task.ArchivedAt is null
        && task.Status is not (WorkStatus.Queued or WorkStatus.Running)
        && current.Kind != StageKind.Terminal;

    /// <summary>La tarjeta muestra el estado «El PR tiene conflictos» mientras el pull request no se integre.</summary>
    public static bool ShowConflicts(TaskItem task) =>
        task.PullRequestHasConflicts && task.PullRequestNumber is not null && task.PullRequestMergedAt is null;
}
