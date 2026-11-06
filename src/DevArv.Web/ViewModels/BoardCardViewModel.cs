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

    /// <summary>Muestra el estado «El PR tiene conflictos»: GitHub informó conflictos y el pull request no se ha integrado.</summary>
    public bool ShowConflicts { get; init; }

    /// <summary>Muestra el botón 🔧 para pedir al desarrollador que arregle los conflictos del pull request.</summary>
    public bool CanResolveConflicts { get; init; }

    /// <summary>El desarrollador está arreglando (o tiene en cola arreglar) los conflictos del pull request.</summary>
    public bool ResolvingConflicts { get; init; }
}
