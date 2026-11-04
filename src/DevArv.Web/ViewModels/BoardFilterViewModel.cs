namespace DevArv.Web.ViewModels;

/// <summary>Filtros del tablero Kanban que el PageModel recibe por query string.</summary>
public sealed class BoardFilterViewModel
{
    public int? ProjectID { get; init; }
    public string? WorkerName { get; init; }
    public string? Query { get; init; }
    public bool Archived { get; init; }
    public bool Waiting { get; init; }
    public bool Compact { get; init; }
    public bool HideEmpty { get; init; }
}
