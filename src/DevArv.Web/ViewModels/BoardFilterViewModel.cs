using Microsoft.AspNetCore.Mvc;

namespace DevArv.Web.ViewModels;

/// <summary>
/// Filtros del tablero Kanban. El PageModel los recibe por query string (los nombres de la URL se conservan) y
/// los pasa tal cual al Repository.
/// </summary>
public sealed class BoardFilterViewModel
{
    [FromQuery(Name = "project")] public int? ProjectID { get; set; }
    [FromQuery(Name = "worker")] public string? WorkerName { get; set; }
    [FromQuery(Name = "q")] public string? Query { get; set; }
    [FromQuery(Name = "archived")] public bool Archived { get; set; }
    [FromQuery(Name = "waiting")] public bool Waiting { get; set; }
    [FromQuery(Name = "compact")] public bool Compact { get; set; }
    [FromQuery(Name = "hideEmpty")] public bool HideEmpty { get; set; }

    /// <summary>Tarea cuyo detalle se abre al cargar el tablero (parámetro «task» de la URL).</summary>
    [FromQuery(Name = "task")] public int? OpenTaskID { get; set; }
}
