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
    [BindProperty(SupportsGet = true, Name = "project")] public int? ProjectFilter { get; set; }
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true, Name = "worker")] public string? WorkerFilter { get; set; }
    [BindProperty(SupportsGet = true)] public bool Compact { get; set; }
    [BindProperty(SupportsGet = true)] public bool HideEmpty { get; set; }
    [BindProperty(SupportsGet = true)] public bool Archived { get; set; }
    [BindProperty(SupportsGet = true)] public bool Waiting { get; set; }
    [BindProperty(SupportsGet = true, Name = "task")] public int? OpenTask { get; set; }

    public List<Project> Projects { get; private set; } = [];
    public List<WorkerNode> Workers { get; private set; } = [];
    public int WaitingCount { get; private set; }
    public BoardViewModel Board { get; private set; } = new();
    public Clock Clock => ClockService;

    public async Task OnGetAsync(CancellationToken CT)
    {
        Projects = await ProjectsRepository.GetActiveAsync(CT);
        Workers = await WorkersRepository.GetEnabledAsync(CT);
        WaitingCount = await TasksRepository.CountWaitingAsync(CT);
        Board = await LoadBoardAsync(CT);
    }

    /// <summary>Fragmento HTML del tablero; el navegador lo pide cada pocos segundos.</summary>
    public async Task<IActionResult> OnGetBoardAsync(CancellationToken CT)
    {
        Board = await LoadBoardAsync(CT);
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

    private Task<BoardViewModel> LoadBoardAsync(CancellationToken CT) =>
        TasksRepository.GetBoardAsync(
            Filter: new BoardFilterViewModel
            {
                ProjectID = ProjectFilter,
                WorkerName = WorkerFilter,
                Query = Q,
                Archived = Archived,
                Waiting = Waiting,
                Compact = Compact,
                HideEmpty = HideEmpty,
            },
            CT: CT);

    public string FilterUrl(int? Project = null, string? Worker = null, bool? Waiting = null, bool? Compact = null,
        bool? HideEmpty = null, bool? Archived = null, bool ClearProject = false, bool ClearWorker = false)
    {
        var query = new Dictionary<string, string?>
        {
            ["project"] = ClearProject ? null : (Project ?? ProjectFilter)?.ToString(),
            ["worker"] = ClearWorker ? null : Worker ?? WorkerFilter,
            ["waiting"] = (Waiting ?? this.Waiting) ? "true" : null,
            ["compact"] = (Compact ?? this.Compact) ? "true" : null,
            ["hideEmpty"] = (HideEmpty ?? this.HideEmpty) ? "true" : null,
            ["archived"] = (Archived ?? this.Archived) ? "true" : null,
            ["q"] = string.IsNullOrWhiteSpace(Q) ? null : Q,
        };
        return Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString("/",
            query.Where(kv => kv.Value is not null).Select(kv => new KeyValuePair<string, string?>(kv.Key, kv.Value)));
    }
}
