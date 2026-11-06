using DevArv.Contracts.Models;
using DevArv.Web.DataModels;

namespace DevArv.Web.ViewModels;

/// <summary>Pantalla de detalle de una tarea: la tarea, listas para los selectores y la ejecución seleccionada.</summary>
public sealed class TaskDetailsViewModel
{
    public required TaskItem Item { get; init; }
    public required List<Stage> Stages { get; init; }
    public required List<WorkerNode> Workers { get; init; }
    public TaskRun? SelectedRun { get; init; }
    public List<ChangedFile> SelectedRunFiles { get; init; } = [];

    /// <summary>GitHub informó conflictos en el pull request de la tarea y aún no se integra.</summary>
    public bool ShowConflicts { get; init; }

    /// <summary>Se puede pedir al desarrollador que arregle los conflictos del pull request (botón 🔧).</summary>
    public bool CanResolveConflicts { get; init; }

    /// <summary>El desarrollador está arreglando (o tiene en cola arreglar) los conflictos del pull request.</summary>
    public bool ResolvingConflicts { get; init; }
}
