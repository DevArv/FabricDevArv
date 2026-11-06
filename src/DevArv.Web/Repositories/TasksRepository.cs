using System.Text.Json;
using System.Text.RegularExpressions;
using DevArv.Contracts.Models;
using DevArv.Web.DataModels;
using DevArv.Web.DatabaseConnection;
using DevArv.Web.Enums.Runs;
using DevArv.Web.Enums.Tasks;
using DevArv.Web.Enums.Workflow;
using DevArv.Web.Helpers;
using DevArv.Web.Services;
using DevArv.Web.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace DevArv.Web.Repositories;

public class TasksRepository(AppDbContext db, AttachmentStorage storage, ILogger<TasksRepository> logger)
{
    private const int MAX_BOARD_TASKS = 1000;

    /// <summary>Estados de una ejecución en cola o en curso (se traduce a SQL con <c>Contains</c>).</summary>
    private static readonly RunStatus[] ActiveRunStatuses = [RunStatus.Queued, RunStatus.Running];

    public Task<int> CountWaitingAsync(CancellationToken ct) =>
        db.Tasks.CountAsync(t => t.ArchivedAt == null && t.Status == WorkStatus.WaitingApproval, ct);

    /// <summary>
    /// Tareas que el agente desarrollador (columna de agente que hace commit y push) está ejecutando ahora,
    /// en todos los proyectos.
    /// </summary>
    public Task<int> CountDevelopingAsync(CancellationToken ct) =>
        db.Tasks.CountAsync(t => t.ArchivedAt == null
                                 && t.Status == WorkStatus.Running
                                 && t.Stage.Kind == StageKind.Agent
                                 && t.Stage.PushChanges, ct);

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
        var now = DateTimeOffset.UtcNow;
        var developingSince = await GetDevelopingSinceAsync(stages, tasks, ct);
        var stagesById = stages.ToDictionary(s => s.Id);
        var resolvingConflicts = await GetResolvingConflictsAsync(tasks, ct);
        var byStage = tasks
            .Select(t => new BoardCardViewModel
            {
                Item = t,
                DevelopmentElapsed = developingSince.TryGetValue(t.Id, out var since) && now > since ? now - since : null,
                ShowConflicts = PullRequestConflictRule.ShowConflicts(t),
                CanResolveConflicts = stagesById.TryGetValue(t.StageId, out var stage)
                                      && PullRequestConflictRule.CanResolve(t, stage),
                ResolvingConflicts = resolvingConflicts.Contains(t.Id),
            })
            .GroupBy(c => c.Item.StageId)
            .ToDictionary(g => g.Key, g => g.ToList());

        return new BoardViewModel
        {
            Compact = filter.Compact,
            HideEmpty = filter.HideEmpty,
            GeneratedAt = now,
            DevelopingCount = await CountDevelopingAsync(ct),
            Columns = stages.Select(s => new BoardColumnViewModel
            {
                Stage = s,
                Tasks = byStage.GetValueOrDefault(s.Id) ?? [],
                SuccessName = s.SuccessStageId is { } ok ? names.GetValueOrDefault(ok) : null,
                RejectName = s.RejectStageId is { } ko ? names.GetValueOrDefault(ko) : null,
            }).ToList(),
        };
    }

    /// <summary>
    /// Para las tareas que el agente desarrollador (columna de agente que hace commit y push) está implementando
    /// ahora, el momento en que una PC tomó la ejecución en curso de esa columna. Clave: id de la tarea.
    /// </summary>
    private async Task<Dictionary<int, DateTimeOffset>> GetDevelopingSinceAsync(
        List<Stage> stages, List<TaskItem> tasks, CancellationToken ct)
    {
        var developerStages = stages.Where(s => s.Kind == StageKind.Agent && s.PushChanges).Select(s => s.Id).ToHashSet();
        var running = tasks.Where(t => t.Status == WorkStatus.Running && developerStages.Contains(t.StageId))
            .ToDictionary(t => t.Id, t => t.StageId);
        if (running.Count == 0) return new Dictionary<int, DateTimeOffset>();

        var ids = running.Keys.ToList();
        var runs = await db.TaskRuns.AsNoTracking()
            .Where(r => ids.Contains(r.TaskId) && r.Status == RunStatus.Running && r.StartedAt != null)
            .Select(r => new { r.TaskId, r.StageId, StartedAt = r.StartedAt!.Value })
            .ToListAsync(ct);

        return runs.Where(r => running[r.TaskId] == r.StageId)
            .GroupBy(r => r.TaskId)
            .ToDictionary(g => g.Key, g => g.Max(r => r.StartedAt));
    }

    /// <summary>
    /// Ids de las tareas en cola o en ejecución cuya ejecución activa es un arreglo de conflictos del pull request
    /// (<see cref="TaskRun.ResolveConflicts"/>).
    /// </summary>
    private async Task<HashSet<int>> GetResolvingConflictsAsync(List<TaskItem> tasks, CancellationToken ct)
    {
        var ids = tasks.Where(t => t.Status is WorkStatus.Queued or WorkStatus.Running).Select(t => t.Id).ToList();
        if (ids.Count == 0) return [];

        var resolving = await db.TaskRuns.AsNoTracking()
            .Where(r => ids.Contains(r.TaskId) && r.ResolveConflicts && ActiveRunStatuses.Contains(r.Status))
            .Select(r => r.TaskId)
            .Distinct()
            .ToListAsync(ct);
        return resolving.ToHashSet();
    }

    /// <summary>
    /// Formulario de nueva tarea con el proyecto activo indicado (o el primero por nombre) y su rama por defecto.
    /// Devuelve null si no hay proyectos activos.
    /// </summary>
    public async Task<TaskCreateViewModel?> NewCreateAsync(int? projectId, CancellationToken ct)
    {
        var active = db.Projects.AsNoTracking().Where(p => p.IsActive);
        var project = (projectId is null ? null : await active.FirstOrDefaultAsync(p => p.Id == projectId, ct))
                      ?? await active.OrderBy(p => p.Name).FirstOrDefaultAsync(ct);
        return project is null
            ? null
            : new TaskCreateViewModel { ProjectId = project.Id, BaseBranch = project.DefaultBranch };
    }

    /// <summary>
    /// Pantalla de detalle: la tarea, las columnas, las PCs habilitadas y la ejecución elegida
    /// (<paramref name="runId"/> o la más reciente) con sus archivos cambiados. Devuelve null si la tarea no existe.
    /// </summary>
    public async Task<TaskDetailsViewModel?> GetDetailsViewAsync(int id, long? runId, CancellationToken ct)
    {
        var task = await GetDetailsAsync(id, ct);
        if (task is null) return null;

        var selectedRun = task.Runs.FirstOrDefault(r => r.Id == runId) ?? task.Runs.FirstOrDefault();
        return new TaskDetailsViewModel
        {
            Item = task,
            Stages = await db.Stages.AsNoTracking().OrderBy(s => s.Order).ToListAsync(ct),
            Workers = await db.Workers.AsNoTracking().Where(w => w.IsEnabled).OrderBy(w => w.Name).ToListAsync(ct),
            SelectedRun = selectedRun,
            SelectedRunFiles = ParseChangedFiles(selectedRun?.ChangedFilesJson),
            ShowConflicts = PullRequestConflictRule.ShowConflicts(task),
            CanResolveConflicts = PullRequestConflictRule.CanResolve(task, task.Stage),
            // GetDetailsAsync ordena las ejecuciones de la más reciente a la más antigua.
            ResolvingConflicts = task.Runs.FirstOrDefault() is { ResolveConflicts: true } last
                                 && ActiveRunStatuses.Contains(last.Status),
        };
    }

    private static List<ChangedFile> ParseChangedFiles(string? json)
    {
        if (json is null) return [];
        try { return JsonSerializer.Deserialize<List<ChangedFile>>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    /// <summary>Tarea con todo su detalle (proyecto, columna, adjuntos, comentarios, eventos y ejecuciones más recientes primero).</summary>
    private async Task<TaskItem?> GetDetailsAsync(int id, CancellationToken ct)
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

    /// <summary>
    /// Elimina definitivamente una tarea con sus ejecuciones, comentarios, eventos y adjuntos (también los archivos en disco).
    /// Lanza WorkflowException si un agente o el sistema la está ejecutando: hay que cancelarla primero.
    /// </summary>
    /// <remarks>
    /// La comprobación y el borrado van en una transacción que bloquea (FOR UPDATE) la tarea y sus ejecuciones.
    /// El reclamo de una ejecución (WorkflowService.TryMarkRunningAsync) actualiza la fila de la ejecución, así que
    /// o bien espera a que termine el borrado (y ya no encuentra la fila), o bien se confirmó antes de que tomáramos
    /// el bloqueo y la comprobación posterior ve la ejecución en curso y rechaza el borrado.
    /// </remarks>
    public async Task DeleteAsync(TaskItem task, string actor, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        await LockTaskAndRunsAsync(task.Id, ct);

        if (await db.TaskRuns.AnyAsync(r => r.TaskId == task.Id && r.Status == RunStatus.Running, ct))
            throw new WorkflowException("La tarea se está ejecutando. Cancela la ejecución y espera a que termine antes de eliminarla.");

        var attachments = await db.TaskAttachments.Where(a => a.TaskId == task.Id).ToListAsync(ct);
        db.Tasks.Remove(task);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        // La tarea ya no existe: un archivo que no se pueda borrar no debe romper la operación ni impedir borrar los demás.
        foreach (var attachment in attachments)
        {
            try
            {
                storage.Delete(attachment);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "No se pudo borrar del disco el adjunto {AttachmentId} de la tarea #{TaskId}.", attachment.Id, task.Id);
            }
        }

        logger.LogInformation("{Actor} eliminó la tarea #{TaskId} «{Title}».", actor, task.Id, task.Title);
    }

    /// <summary>
    /// Bloquea (FOR UPDATE) la fila de la tarea y las de sus ejecuciones dentro de la transacción actual.
    /// Es virtual sólo para poder probar <see cref="DeleteAsync"/> sin PostgreSQL: el proveedor en memoria no ejecuta SQL.
    /// </summary>
    protected virtual async Task LockTaskAndRunsAsync(int taskId, CancellationToken ct)
    {
        await db.Database.SqlQuery<int>($"""SELECT "Id" AS "Value" FROM "Tasks" WHERE "Id" = {taskId} FOR UPDATE""").ToListAsync(ct);
        await db.Database.SqlQuery<long>($"""SELECT "Id" AS "Value" FROM "TaskRuns" WHERE "TaskId" = {taskId} FOR UPDATE""").ToListAsync(ct);
    }
}
