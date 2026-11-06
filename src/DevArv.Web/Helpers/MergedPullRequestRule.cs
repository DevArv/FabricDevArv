using DevArv.Web.DataModels;
using DevArv.Web.Enums.Workflow;

namespace DevArv.Web.Helpers;

/// <summary>Cuándo una tarea cuyo pull request se integró debe entrar a la columna destino del merge.</summary>
public static class MergedPullRequestRule
{
    /// <summary>
    /// Sólo se mueven las tareas que están en una columna anterior a <paramref name="Destination"/> en el tablero
    /// y que no están finalizadas: así no se regresa una tarea que ya avanzó (p. ej. «Pendiente Prueba») ni se
    /// reabre una descartada.
    /// </summary>
    public static bool ShouldMove(Stage Current, Stage Destination) =>
        Current.ID != Destination.ID
        && Current.Kind != StageKind.TERMINAL
        && Current.Order < Destination.Order;
}
