using DevArv.Web.DataModels;

namespace DevArv.Web.ViewModels;

/// <summary>Tarjeta del tablero: la tarea y los datos calculados para mostrarla.</summary>
public sealed class BoardCardViewModel
{
    public required TaskItem Item { get; init; }

    /// <summary>
    /// Tiempo que lleva el agente desarrollador implementando la tarea (desde que una PC tomó la ejecución
    /// en curso hasta que se generó el tablero). Nulo si la tarea no se está implementando ahora.
    /// </summary>
    public TimeSpan? DevelopmentElapsed { get; init; }
}
