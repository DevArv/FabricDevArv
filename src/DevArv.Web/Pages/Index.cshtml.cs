using DevArv.Web.Data;
using DevArv.Web.Domain;
using DevArv.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace DevArv.Web.Pages;

public sealed class BoardColumn
{
    public required Stage Stage { get; init; }
    public required List<TaskItem> Tasks { get; init; }
    public string? SuccessName { get; init; }
    public string? RejectName { get; init; }
}

public sealed class BoardViewModel
{
    public List<BoardColumn> Columns { get; init; } = [];
    public bool Compact { get; init; }
    public bool HideEmpty { get; init; }
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class IndexModel(AppDbContext db, WorkflowService workflow, Clock clock) : PageModel
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
        Projects = await db.Projects.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.Name).ToListAsync(ct);
        Workers = await db.Workers.AsNoTracking().Where(w => w.IsEnabled).OrderBy(w => w.Name).ToListAsync(ct);
        WaitingCount = await db.Tasks.CountAsync(t => t.ArchivedAt == null && t.Status == WorkStatus.WaitingApproval, ct);
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
        var task = await workflow.LoadTaskAsync(taskId, ct);
        if (task is null) return NotFound();
        // Soltar en la misma columna no hace nada (para re-ejecutar un agente se usa «Ejecutar de nuevo»).
        if (task.StageId == stageId) return new JsonResult(new { ok = true });
        try
        {
            await workflow.MoveAsync(task, stageId, User.Identity?.Name ?? "?", ct);
            return new JsonResult(new { ok = true });
        }
        catch (WorkflowException ex)
        {
            return BadRequest(new { ok = false, error = ex.Message });
        }
    }

    private async Task<BoardViewModel> LoadBoardAsync(CancellationToken ct)
    {
        var stages = await db.Stages.AsNoTracking().OrderBy(s => s.Order).ToListAsync(ct);
        var names = stages.ToDictionary(s => s.Id, s => s.Name);

        var query = db.Tasks.AsNoTracking().Include(t => t.Project)
            .Where(t => Archived ? t.ArchivedAt != null : t.ArchivedAt == null);
        if (ProjectFilter is { } projectId) query = query.Where(t => t.ProjectId == projectId);
        if (!string.IsNullOrWhiteSpace(WorkerFilter)) query = query.Where(t => t.LastWorkerName == WorkerFilter);
        if (Waiting) query = query.Where(t => t.Status == WorkStatus.WaitingApproval);
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var term = Q.Trim().TrimStart('#');
            var like = "%" + term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = int.TryParse(term, out var id)
                ? query.Where(t => t.Id == id || EF.Functions.ILike(t.Title, like))
                : query.Where(t => EF.Functions.ILike(t.Title, like) || EF.Functions.ILike(t.Description, like));
        }

        var tasks = await query
            .OrderByDescending(t => t.Priority)
            .ThenBy(t => t.CreatedAt)
            .Take(1000)
            .ToListAsync(ct);
        var byStage = tasks.GroupBy(t => t.StageId).ToDictionary(g => g.Key, g => g.ToList());

        return new BoardViewModel
        {
            Compact = Compact,
            HideEmpty = HideEmpty,
            Columns = stages.Select(s => new BoardColumn
            {
                Stage = s,
                Tasks = byStage.GetValueOrDefault(s.Id) ?? [],
                SuccessName = s.SuccessStageId is { } ok ? names.GetValueOrDefault(ok) : null,
                RejectName = s.RejectStageId is { } ko ? names.GetValueOrDefault(ko) : null,
            }).ToList(),
        };
    }

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
