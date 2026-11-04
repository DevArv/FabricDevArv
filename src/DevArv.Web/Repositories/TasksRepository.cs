using System.Text.RegularExpressions;
using DevArv.Web.DataModels;
using DevArv.Web.DatabaseConnection;
using DevArv.Web.Enums.Tasks;
using DevArv.Web.Helpers;
using DevArv.Web.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace DevArv.Web.Repositories;

public class TasksRepository(AppDbContext db)
{
    private const int MAX_BOARD_TASKS = 1000;

    public Task<int> CountWaitingAsync(CancellationToken ct) =>
        db.Tasks.CountAsync(t => t.ArchivedAt == null && t.Status == WorkStatus.WaitingApproval, ct);

    public async Task<BoardViewModel> GetBoardAsync(BoardFilterViewModel filter, CancellationToken ct)
    {
        var stages = await db.Stages.AsNoTracking().OrderBy(s => s.Order).ToListAsync(ct);
        var names = stages.ToDictionary(s => s.Id, s => s.Name);

        var query = db.Tasks.AsNoTracking().Include(t => t.Project)
            .Where(t => filter.Archived ? t.ArchivedAt != null : t.ArchivedAt == null);
        if (filter.ProjectID is { } projectId) query = query.Where(t => t.ProjectId == projectId);
        if (!string.IsNullOrWhiteSpace(filter.WorkerName)) query = query.Where(t => t.LastWorkerName == filter.WorkerName);
        if (filter.Waiting) query = query.Where(t => t.Status == WorkStatus.WaitingApproval);
        if (!string.IsNullOrWhiteSpace(filter.Query))
        {
            var term = filter.Query.Trim().TrimStart('#');
            var like = "%" + term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = int.TryParse(term, out var id)
                ? query.Where(t => t.Id == id || EF.Functions.ILike(t.Title, like))
                : query.Where(t => EF.Functions.ILike(t.Title, like) || EF.Functions.ILike(t.Description, like));
        }

        var tasks = await query
            .OrderByDescending(t => t.Priority)
            .ThenBy(t => t.CreatedAt)
            .Take(MAX_BOARD_TASKS)
            .ToListAsync(ct);
        var byStage = tasks.GroupBy(t => t.StageId).ToDictionary(g => g.Key, g => g.ToList());

        return new BoardViewModel
        {
            Compact = filter.Compact,
            HideEmpty = filter.HideEmpty,
            Columns = stages.Select(s => new BoardColumnViewModel
            {
                Stage = s,
                Tasks = byStage.GetValueOrDefault(s.Id) ?? [],
                SuccessName = s.SuccessStageId is { } ok ? names.GetValueOrDefault(ok) : null,
                RejectName = s.RejectStageId is { } ko ? names.GetValueOrDefault(ko) : null,
            }).ToList(),
        };
    }

    /// <summary>Tarea con todo su detalle (proyecto, columna, adjuntos, comentarios, eventos y ejecuciones más recientes primero).</summary>
    public async Task<TaskItem?> GetDetailsAsync(int id, CancellationToken ct)
    {
        var task = await db.Tasks
            .Include(t => t.Project)
            .Include(t => t.Stage)
            .Include(t => t.PreferredWorker)
            .Include(t => t.Attachments)
            .Include(t => t.Comments)
            .Include(t => t.Events)
            .Include(t => t.Runs).ThenInclude(r => r.Worker)
            .AsSplitQuery()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, ct);
        if (task is null) return null;

        task.Runs = task.Runs.OrderByDescending(r => r.Id).ToList();
        return task;
    }

    /// <summary>Edita los campos de una tarea ya cargada con seguimiento (ver WorkflowService.LoadTaskAsync).</summary>
    public async Task UpdateAsync(TaskItem task, TaskUpdateViewModel record, string actor, CancellationToken ct)
    {
        if (record.ModelOverride is not null && !Regex.IsMatch(record.ModelOverride.Trim(), @"^[A-Za-z0-9.\-_\[\]]*$"))
            throw new ValidationFailedException(nameof(record.ModelOverride), "Nombre de modelo inválido.");
        if (record.PreferredWorkerId is not null && !await db.Workers.AnyAsync(w => w.Id == record.PreferredWorkerId, ct))
            throw new ValidationFailedException(nameof(record.PreferredWorkerId), "La PC no existe.");

        if (!string.IsNullOrWhiteSpace(record.Title)) task.Title = record.Title.Trim()[..Math.Min(record.Title.Trim().Length, 200)];
        if (record.Description is not null) task.Description = record.Description.Trim();
        task.Priority = record.Priority;
        task.Complexity = record.Complexity;
        task.Labels = TaskLabelsHelper.Normalize(record.Labels);
        task.ModelOverride = string.IsNullOrWhiteSpace(record.ModelOverride) ? null : record.ModelOverride.Trim();
        task.PreferredWorkerId = record.PreferredWorkerId;
        task.UpdatedAt = DateTimeOffset.UtcNow;
        db.TaskEvents.Add(new TaskEvent { TaskId = task.Id, Actor = actor, Message = $"{actor} editó la tarea" });
        await db.SaveChangesAsync(ct);
    }
}
