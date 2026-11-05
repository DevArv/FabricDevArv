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
}
