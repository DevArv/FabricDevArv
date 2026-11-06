using DevArv.Web.DataModels;
using DevArv.Web.Helpers;
using DevArv.Web.Repositories;
using DevArv.Web.Services;
using DevArv.Web.ViewModels;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;

namespace DevArv.Web.Pages;

public sealed class IndexModel(
    ProjectsRepository ProjectsRepository, WorkersRepository WorkersRepository, TasksRepository TasksRepository,
    WorkflowService WorkflowService, Clock ClockService) : PageModel
{
    /// <summary>Filtros de la URL; la vista los usa para marcar los chips activos y armar los enlaces.</summary>
    public BoardFilterViewModel Filter { get; private set; } = new();

    public List<Project> Projects { get; private set; } = [];
    public List<WorkerNode> Workers { get; private set; } = [];
    public int WaitingCount { get; private set; }
    public BoardViewModel Board { get; private set; } = new();
    public Clock Clock => ClockService;

    public async Task OnGetAsync(BoardFilterViewModel Filter, CancellationToken CT)
    {
        this.Filter = Filter;
        Projects = await ProjectsRepository.GetActiveAsync(CT);
        Workers = await WorkersRepository.GetEnabledAsync(CT);
        WaitingCount = await TasksRepository.CountWaitingAsync(CT);
        Board = await TasksRepository.GetBoardAsync(
            Filter: Filter,
            CT: CT);
    }

    /// <summary>Fragmento HTML del tablero; el navegador lo pide cada pocos segundos.</summary>
    public async Task<IActionResult> OnGetBoardAsync(BoardFilterViewModel Filter, CancellationToken CT)
    {
        this.Filter = Filter;
        Board = await TasksRepository.GetBoardAsync(
            Filter: Filter,
            CT: CT);
        return Partial("_Board", Board);
    }

    /// <summary>Arrastrar y soltar una tarjeta en otra columna.</summary>
    public async Task<IActionResult> OnPostMoveAsync(int TaskID, int StageID, CancellationToken CT)
    {
        try
        {
            var found = await WorkflowService.DropOnStageAsync(
                TaskID: TaskID,
                StageID: StageID,
                Actor: User.Identity?.Name ?? "?",
                CT: CT);
            if (!found) return NotFound();
            return new JsonResult(new { ok = true });
        }
        catch (WorkflowException ex)
        {
            return BadRequest(new { ok = false, error = ex.Message });
        }
    }

    public string FilterUrl(int? Project = null, string? Worker = null, bool? Waiting = null, bool? Compact = null,
        bool? HideEmpty = null, bool? Archived = null, bool ClearProject = false, bool ClearWorker = false)
    {
        var query = new Dictionary<string, string?>
        {
            ["project"] = ClearProject ? null : (Project ?? Filter.ProjectID)?.ToString(),
            ["worker"] = ClearWorker ? null : Worker ?? Filter.WorkerName,
            ["waiting"] = (Waiting ?? this.Filter.Waiting) ? "true" : null,
            ["compact"] = (Compact ?? this.Filter.Compact) ? "true" : null,
            ["hideEmpty"] = (HideEmpty ?? this.Filter.HideEmpty) ? "true" : null,
            ["archived"] = (Archived ?? this.Filter.Archived) ? "true" : null,
            ["q"] = string.IsNullOrWhiteSpace(Filter.Query) ? null : Filter.Query,
        };
        return Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString("/",
            query.Where(kv => kv.Value is not null).Select(kv => new KeyValuePair<string, string?>(kv.Key, kv.Value)));
    }
}
