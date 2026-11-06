using DevArv.Web.DataModels;
using DevArv.Web.Helpers;
using DevArv.Web.Repositories;
using DevArv.Web.Services;
using DevArv.Web.ViewModels;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;

namespace DevArv.Web.Pages;

public sealed class IndexModel(
    ProjectsRepository projects, WorkersRepository workers, TasksRepository tasks,
    WorkflowService workflow, Clock clock) : PageModel
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
    public Clock Clock => clock;

    public async Task OnGetAsync(CancellationToken ct)
    {
        Projects = await projects.GetActiveAsync(ct);
        Workers = await workers.GetEnabledAsync(ct);
        WaitingCount = await tasks.CountWaitingAsync(ct);
        Board = await LoadBoardAsync(ct);
    }

    /// <summary>Fragmento HTML del tablero; el navegador lo pide cada pocos segundos.</summary>
    public async Task<IActionResult> OnGetBoardAsync(CancellationToken ct)
    {
        Board = await LoadBoardAsync(ct);
        return Partial("_Board", Board);
    }

    /// <summary>Arrastrar y soltar una tarjeta en otra columna.</summary>
    public async Task<IActionResult> OnPostMoveAsync(int taskId, int stageId, CancellationToken ct)
    {
        try
        {
            if (!await workflow.DropOnStageAsync(taskId, stageId, User.Identity?.Name ?? "?", ct)) return NotFound();
            return new JsonResult(new { ok = true });
        }
        catch (WorkflowException ex)
        {
            return BadRequest(new { ok = false, error = ex.Message });
        }
    }

    /// <summary>Botón 🔧 de la tarjeta: regresa la tarea al desarrollador para arreglar los conflictos de su pull request.</summary>
    public async Task<IActionResult> OnPostResolveConflictsAsync(int taskId, CancellationToken ct)
    {
        var task = await workflow.LoadTaskAsync(taskId, ct);
        if (task is null) return NotFound();
        try
        {
            await workflow.ResolveConflictsAsync(task, User.Identity?.Name ?? "?", ct);
            return new JsonResult(new { ok = true });
        }
        catch (WorkflowException ex)
        {
            return BadRequest(new { ok = false, error = ex.Message });
        }
    }

    private Task<BoardViewModel> LoadBoardAsync(CancellationToken ct) =>
        tasks.GetBoardAsync(new BoardFilterViewModel
        {
            ProjectID = ProjectFilter,
            WorkerName = WorkerFilter,
            Query = Q,
            Archived = Archived,
            Waiting = Waiting,
            Compact = Compact,
            HideEmpty = HideEmpty,
        }, ct);

    public string FilterUrl(int? project = null, string? worker = null, bool? waiting = null, bool? compact = null,
        bool? hideEmpty = null, bool? archived = null, bool clearProject = false, bool clearWorker = false)
    {
        var query = new Dictionary<string, string?>
        {
            ["project"] = clearProject ? null : (project ?? ProjectFilter)?.ToString(),
            ["worker"] = clearWorker ? null : worker ?? WorkerFilter,
            ["waiting"] = (waiting ?? Waiting) ? "true" : null,
            ["compact"] = (compact ?? Compact) ? "true" : null,
            ["hideEmpty"] = (hideEmpty ?? HideEmpty) ? "true" : null,
            ["archived"] = (archived ?? Archived) ? "true" : null,
            ["q"] = string.IsNullOrWhiteSpace(Q) ? null : Q,
        };
        return Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString("/",
            query.Where(kv => kv.Value is not null).Select(kv => new KeyValuePair<string, string?>(kv.Key, kv.Value)));
    }
}
