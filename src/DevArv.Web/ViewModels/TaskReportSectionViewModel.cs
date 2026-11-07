namespace DevArv.Web.ViewModels;

/// <summary>
/// Sección del resumen del desarrollador que se muestra en una pestaña del detalle de la tarea
/// («Pruebas unitarias» o «Pruebas de QA»), con la ejecución de la que salió.
/// </summary>
public sealed class TaskReportSectionViewModel
{
    /// <summary>Markdown de la sección, sin el título.</summary>
    public required string Content { get; init; }

    public required long RunID { get; init; }
    public required string StageName { get; init; }
    public DateTimeOffset? FinishedAt { get; init; }
}
