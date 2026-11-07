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

    /// <summary>Adjuntos de la definición de la tarea (sin las capturas).</summary>
    public List<TaskAttachment> Attachments { get; init; } = [];

    /// <summary>
    /// La tarea llegó a «Creador de PR»: el detalle de la ejecución se muestra en las pestañas Log, Pruebas unitarias,
    /// Capturas y Pruebas de QA.
    /// </summary>
    public bool ShowReportTabs { get; init; }

    /// <summary>Líneas del log de la ejecución seleccionada.</summary>
    public int SelectedRunLogLines { get; init; }

    /// <summary>Resultado de las pruebas unitarias que reportó el desarrollador; null si no lo reportó.</summary>
    public TaskReportSectionViewModel? UnitTests { get; init; }

    /// <summary>Guía funcional para QA que documentó el desarrollador; null si no la documentó.</summary>
    public TaskReportSectionViewModel? QaTests { get; init; }

    /// <summary>Capturas con los cambios aplicados (imágenes), de la más antigua a la más reciente.</summary>
    public List<TaskAttachment> Captures { get; init; } = [];
}
