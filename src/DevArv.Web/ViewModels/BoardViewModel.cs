namespace DevArv.Web.ViewModels;

public sealed class BoardViewModel
{
    public List<BoardColumnViewModel> Columns { get; init; } = [];
    public bool Compact { get; init; }
    public bool HideEmpty { get; init; }
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Tareas en ejecución del desarrollador en todos los proyectos (sin aplicar filtros); actualiza el badge de la barra.</summary>
    public int DevelopingCount { get; init; }
}
