using System.Text.Json;
using DevArv.Contracts.Models;
using DevArv.Web.DataModels;
using DevArv.Web.DatabaseConnection;
using DevArv.Web.Enums.Runs;
using DevArv.Web.Enums.Tasks;
using DevArv.Web.Enums.Workflow;
using DevArv.Web.Helpers;
using DevArv.Web.Models;
using DevArv.Web.Repositories;
using DevArv.Web.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace DevArv.Web.Services;

/// <summary>
/// Reglas del flujo Kanban: entrar a una columna, aprobar, pedir cambios, reintentar,
/// y procesar el resultado de las ejecuciones de agentes y del sistema.
/// </summary>
public sealed class WorkflowService(AppDbContext db, AttachmentStorage storage, ILogger<WorkflowService> logger)
{
    private static readonly TimeSpan RunMaxLogAge = TimeSpan.FromMinutes(5);

    public Task<TaskItem?> LoadTaskAsync(int taskId, CancellationToken ct) =>
        db.Tasks.Include(t => t.Stage).Include(t => t.Project).FirstOrDefaultAsync(t => t.Id == taskId, ct);

    public async Task<Stage> GetInitialStageAsync(CancellationToken ct) =>
        await db.Stages.OrderByDescending(s => s.IsInitial).ThenBy(s => s.Order).FirstOrDefaultAsync(ct)
        ?? throw new WorkflowException("No hay columnas configuradas en el flujo.");

    /// <summary>
    /// Crea una tarea: valida proyecto y adjuntos, la inserta, guarda los archivos y la hace entrar a su primera columna
    /// (o a la primera de desarrollo si <see cref="TaskCreateViewModel.StartNow"/>). Lanza ValidationFailedException.
    /// </summary>
    public async Task<TaskItem> CreateTaskAsync(
        TaskCreateViewModel record, IReadOnlyCollection<IFormFile> files, string actor, string? userId, CancellationToken ct)
    {
        var errors = new List<(string Field, string Message)>();
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == record.ProjectId && p.IsActive, ct);
        if (project is null) errors.Add(("Input.ProjectId", "Selecciona un proyecto activo."));

        List<ValidatedUpload> uploads = [];
        try
        {
            uploads = await storage.ValidateAsync(files, 0, ct);
        }
        catch (AttachmentValidationException ex)
        {
            errors.Add(("Files", ex.Message));
        }
        if (errors.Count > 0 || project is null) throw new ValidationFailedException(errors);

        var initial = await GetInitialStageAsync(ct);
        var task = new TaskItem
        {
            Title = record.Title.Trim(),
            Description = record.Description?.Trim() ?? "",
            ProjectId = project.Id,
            Project = project,
            StageId = initial.Id,
            BaseBranch = record.BaseBranch.Trim(),
            Priority = record.Priority,
            Complexity = record.Complexity,
            Labels = TaskLabelsHelper.Normalize(record.Labels),
            ModelOverride = string.IsNullOrWhiteSpace(record.ModelOverride) ? null : record.ModelOverride.Trim(),
            PreferredWorkerId = await db.Workers.AnyAsync(w => w.Id == record.PreferredWorkerId && w.IsEnabled, ct)
                ? record.PreferredWorkerId
                : null,
            CreatedById = userId,
            CreatedByName = actor,
        };
        db.Tasks.Add(task);
        await db.SaveChangesAsync(ct);

        db.TaskAttachments.AddRange(await storage.StoreAsync(task.Id, uploads, ct));

        var start = initial;
        if (record.StartNow)
        {
            start = await db.Stages.Where(s => s.Kind == StageKind.Agent && s.PushChanges)
                .OrderBy(s => s.Order).FirstOrDefaultAsync(ct) ?? initial;
        }
        await EnterStageAsync(task, start, actor, $"{actor} creó la tarea en «{start.Name}»", ct);
        await db.SaveChangesAsync(ct);
        return task;
    }

    // ------------------------------------------------------------------ acciones de usuario

    public async Task MoveAsync(TaskItem task, int stageId, string actor, CancellationToken ct)
    {
        var target = await db.Stages.FindAsync([stageId], ct) ?? throw new WorkflowException("La columna no existe.");
        await EnterStageAsync(task, target, actor, $"{actor} movió la tarea a «{target.Name}»", ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task ApproveAsync(TaskItem task, string actor, CancellationToken ct)
    {
        if (task.Stage.Kind is not (StageKind.HumanApproval or StageKind.Manual))
            throw new WorkflowException("Esta columna no se aprueba manualmente.");
        var nextId = task.Stage.SuccessStageId
            ?? throw new WorkflowException($"«{task.Stage.Name}» no tiene columna destino configurada.");
        var next = await db.Stages.FindAsync([nextId], ct) ?? throw new WorkflowException("La columna destino no existe.");
        var verb = task.Stage.Kind == StageKind.HumanApproval ? "aprobó" : "avanzó";
        await EnterStageAsync(task, next, actor, $"{actor} {verb} en «{task.Stage.Name}» → «{next.Name}»", ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task RequestChangesAsync(TaskItem task, string actor, string feedback, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(feedback))
            throw new WorkflowException("Describe los cambios que necesitas.");

        var targetId = task.Stage.RejectStageId
            ?? await db.Stages.Where(s => s.Kind == StageKind.Agent && s.PushChanges)
                .OrderBy(s => s.Order).Select(s => (int?)s.Id).FirstOrDefaultAsync(ct)
            ?? throw new WorkflowException("No hay columna destino para pedir cambios.");
        var target = await db.Stages.FindAsync([targetId], ct) ?? throw new WorkflowException("La columna destino no existe.");

        db.TaskComments.Add(new TaskComment
        {
            TaskId = task.Id, Kind = CommentKind.Feedback, Author = actor, Body = feedback.Trim(),
        });
        await EnterStageAsync(task, target, actor, $"{actor} pidió cambios → «{target.Name}»", ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task AddCommentAsync(TaskItem task, string actor, string body, bool asFeedback, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body)) throw new WorkflowException("El comentario está vacío.");
        db.TaskComments.Add(new TaskComment
        {
            TaskId = task.Id,
            Kind = asFeedback ? CommentKind.Feedback : CommentKind.Comment,
            Author = actor,
            Body = body.Trim(),
        });
        task.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task RetryAsync(TaskItem task, string actor, CancellationToken ct)
    {
        if (!task.Stage.RunsAutomatically)
            throw new WorkflowException("Sólo se reintentan columnas de agente o automáticas.");
        await EnterStageAsync(task, task.Stage, actor, $"{actor} reintentó «{task.Stage.Name}»", ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task CancelAsync(TaskItem task, string actor, CancellationToken ct)
    {
        var cancelled = await CancelActiveRunsAsync(task.Id, ct);
        if (cancelled == 0) throw new WorkflowException("No hay ejecuciones activas.");
        task.Status = WorkStatus.Idle;
        task.LastError = $"Cancelado por {actor}";
        task.UpdatedAt = DateTimeOffset.UtcNow;
        AddEvent(task.Id, actor, $"{actor} canceló la ejecución en «{task.Stage.Name}»");
        await db.SaveChangesAsync(ct);
    }

    public async Task SetArchivedAsync(TaskItem task, bool archived, string actor, CancellationToken ct)
    {
        if (archived) await CancelActiveRunsAsync(task.Id, ct);
        task.ArchivedAt = archived ? DateTimeOffset.UtcNow : null;
        AddEvent(task.Id, actor, archived ? $"{actor} archivó la tarea" : $"{actor} restauró la tarea");
        await db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------ motor

    /// <summary>Coloca la tarea en una columna y, si es automática, encola una ejecución. No guarda.</summary>
    public async Task EnterStageAsync(TaskItem task, Stage stage, string actor, string message, CancellationToken ct)
    {
        await CancelActiveRunsAsync(task.Id, ct);

        if (task.Project is null) await db.Entry(task).Reference(t => t.Project).LoadAsync(ct);

        var now = DateTimeOffset.UtcNow;
        task.StageId = stage.Id;
        task.Stage = stage;
        task.StageEnteredAt = now;
        task.UpdatedAt = now;
        task.LastError = null;

        switch (stage.Kind)
        {
            case StageKind.Agent:
            case StageKind.System:
                if (string.IsNullOrEmpty(task.WorkBranch))
                    task.WorkBranch = $"{task.Project!.BranchPrefix}{task.Id}-{Slug.Create(task.Title)}";
                db.TaskRuns.Add(new TaskRun
                {
                    TaskId = task.Id,
                    StageId = stage.Id,
                    StageName = stage.Name,
                    RunnerKind = stage.Kind,
                    Status = RunStatus.Queued,
                    PinnedWorkerId = stage.Kind == StageKind.Agent ? task.PreferredWorkerId : null,
                    Model = stage.Kind == StageKind.Agent ? (task.ModelOverride ?? stage.Model) : null,
                    CreatedAt = now,
                });
                task.Status = WorkStatus.Queued;
                break;
            case StageKind.HumanApproval:
                task.Status = WorkStatus.WaitingApproval;
                break;
            case StageKind.Terminal:
                task.Status = WorkStatus.Done;
                break;
            default:
                task.Status = WorkStatus.Idle;
                break;
        }

        AddEvent(task.Id, actor, message);
    }

    /// <summary>Un worker reclama la siguiente ejecución de agente en cola (seguro ante concurrencia).</summary>
    public async Task<TaskRun?> ClaimNextAgentRunAsync(WorkerNode worker, CancellationToken ct)
    {
        var candidates = await db.TaskRuns
            .Where(r => r.Status == RunStatus.Queued && r.RunnerKind == StageKind.Agent
                        && (r.PinnedWorkerId == null || r.PinnedWorkerId == worker.Id)
                        && r.Task.ArchivedAt == null && r.Task.Project.IsActive)
            .OrderBy(r => r.CreatedAt)
            .Select(r => r.Id)
            .Take(5)
            .ToListAsync(ct);

        foreach (var id in candidates)
        {
            if (await TryMarkRunningAsync(id, worker.Id, ct))
                return await OnRunStartedAsync(id, worker.Name, ct);
        }
        return null;
    }

    /// <summary>El orquestador del servidor reclama la siguiente ejecución de sistema (crear PR, merge).</summary>
    public async Task<TaskRun?> ClaimNextSystemRunAsync(CancellationToken ct)
    {
        var candidates = await db.TaskRuns
            .Where(r => r.Status == RunStatus.Queued && r.RunnerKind == StageKind.System && r.Task.ArchivedAt == null)
            .OrderBy(r => r.CreatedAt)
            .Select(r => r.Id)
            .Take(5)
            .ToListAsync(ct);

        foreach (var id in candidates)
        {
            if (await TryMarkRunningAsync(id, null, ct))
                return await OnRunStartedAsync(id, "Sistema", ct);
        }
        return null;
    }

    private async Task<bool> TryMarkRunningAsync(long runId, int? workerId, CancellationToken ct)
    {
        DateTimeOffset? now = DateTimeOffset.UtcNow;
        var updated = await db.TaskRuns
            .Where(r => r.Id == runId && r.Status == RunStatus.Queued)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, RunStatus.Running)
                .SetProperty(r => r.WorkerId, workerId)
                .SetProperty(r => r.StartedAt, now)
                .SetProperty(r => r.LastActivityAt, now), ct);
        return updated == 1;
    }

    private async Task<TaskRun> OnRunStartedAsync(long runId, string actor, CancellationToken ct)
    {
        var run = await db.TaskRuns
            .Include(r => r.Task).ThenInclude(t => t.Project)
            .Include(r => r.Task).ThenInclude(t => t.Stage)
            .Include(r => r.Task).ThenInclude(t => t.Attachments)
            .Include(r => r.Task).ThenInclude(t => t.Comments)
            .AsSplitQuery()
            .FirstAsync(r => r.Id == runId, ct);

        run.Task.Status = WorkStatus.Running;
        run.Task.UpdatedAt = DateTimeOffset.UtcNow;
        if (run.RunnerKind == StageKind.Agent) run.Task.LastWorkerName = actor;
        AddEvent(run.TaskId, actor, $"{actor} tomó el pedido «{run.StageName}»");
        await db.SaveChangesAsync(ct);
        return run;
    }

    /// <summary>Registra el resultado de una ejecución y avanza (o devuelve) la tarea según la columna.</summary>
    public async Task CompleteRunAsync(long runId, RunCompletion c, string actor, CancellationToken ct)
    {
        var run = await db.TaskRuns
            .Include(r => r.Task).ThenInclude(t => t.Stage)
            .Include(r => r.Task).ThenInclude(t => t.Project)
            .FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null || run.Status != RunStatus.Running)
        {
            logger.LogWarning("Resultado ignorado para la ejecución {RunId}: no está en curso.", runId);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var task = run.Task;
        run.FinishedAt = now;
        run.LastActivityAt = now;
        run.CostUsd = c.CostUsd;
        run.InputTokens = c.InputTokens;
        run.OutputTokens = c.OutputTokens;
        run.CacheReadTokens = c.CacheReadTokens;
        run.CacheWriteTokens = c.CacheWriteTokens;
        run.NumTurns = c.NumTurns;
        run.SessionId = c.SessionId;
        run.Model = c.Model ?? run.Model;
        run.HeadCommit = c.HeadCommit;
        run.Summary = Clean(c.ResultText);
        run.ChangedFilesJson = c.ChangedFiles.Count > 0 ? JsonSerializer.Serialize(c.ChangedFiles) : null;

        task.TotalCostUsd += c.CostUsd;
        task.TotalTokens += c.InputTokens + c.OutputTokens + c.CacheReadTokens + c.CacheWriteTokens;
        task.UpdatedAt = now;

        // Sólo la ejecución más reciente de la columna actual puede mover la tarea.
        var isCurrent = task.StageId == run.StageId
                        && !await db.TaskRuns.AnyAsync(r => r.TaskId == task.Id && r.Id > run.Id, ct);

        if (c.Cancelled || run.CancelRequested)
        {
            run.Status = RunStatus.Cancelled;
            run.ErrorMessage = Clean(c.ErrorMessage) ?? "Ejecución cancelada";
            if (isCurrent && task.Status is WorkStatus.Running or WorkStatus.Queued)
            {
                task.Status = WorkStatus.Idle;
                task.LastError = "Ejecución cancelada";
            }
            AddEvent(task.Id, actor, $"Se canceló «{run.StageName}»");
            await db.SaveChangesAsync(ct);
            return;
        }

        if (!c.Success)
        {
            await FailAsync(run, task, isCurrent, Clean(c.ErrorMessage) ?? "La ejecución falló sin detalle.", actor, ct);
            return;
        }

        var stage = task.Stage;
        if (isCurrent && stage.PushChanges && c.ChangedFiles.Count == 0)
        {
            await FailAsync(run, task, isCurrent, "El agente terminó sin cambios en la rama respecto a la rama base.", actor, ct);
            return;
        }

        run.Status = RunStatus.Succeeded;
        if (!isCurrent)
        {
            await db.SaveChangesAsync(ct);
            return;
        }

        if (stage.RequiresDecision)
        {
            run.Decision = DecisionParser.Parse(c.ResultText);
            switch (run.Decision)
            {
                case RunDecision.None:
                    task.Status = WorkStatus.Failed;
                    task.LastError = "El agente no emitió veredicto («VEREDICTO: APROBADO» o «VEREDICTO: RECHAZADO»).";
                    AddEvent(task.Id, actor, $"«{stage.Name}» terminó sin veredicto");
                    await db.SaveChangesAsync(ct);
                    return;

                case RunDecision.Rejected:
                    db.TaskComments.Add(new TaskComment
                    {
                        TaskId = task.Id, Kind = CommentKind.ReviewerNote, Author = stage.Name,
                        Body = run.Summary ?? "", RunId = run.Id,
                    });
                    await GoToAsync(task, stage.RejectStageId, actor, $"«{stage.Name}» rechazó", ct);
                    await db.SaveChangesAsync(ct);
                    return;
            }
        }

        var verb = run.Decision == RunDecision.Approved ? "aprobó" : "completó";
        await GoToAsync(task, stage.SuccessStageId, actor, $"«{stage.Name}» {verb}", ct);
        await db.SaveChangesAsync(ct);
    }

    private async Task GoToAsync(TaskItem task, int? stageId, string actor, string reason, CancellationToken ct)
    {
        var next = stageId is null ? null : await db.Stages.FindAsync([stageId.Value], ct);
        if (next is null)
        {
            task.Status = WorkStatus.Done;
            AddEvent(task.Id, actor, $"{reason}; la columna no tiene destino configurado");
            return;
        }
        await EnterStageAsync(task, next, actor, $"{reason} → «{next.Name}»", ct);
    }

    private async Task FailAsync(TaskRun run, TaskItem task, bool isCurrent, string error, string actor, CancellationToken ct)
    {
        run.Status = RunStatus.Failed;
        run.ErrorMessage = error;
        if (isCurrent)
        {
            task.Status = WorkStatus.Failed;
            task.LastError = error;
        }
        AddEvent(task.Id, actor, $"«{run.StageName}» falló: {Truncate(error, 300)}");
        await db.SaveChangesAsync(ct);
    }

    private async Task<int> CancelActiveRunsAsync(int taskId, CancellationToken ct)
    {
        // Incluye las ejecuciones ya agregadas al contexto y aún no guardadas.
        var active = await db.TaskRuns
            .Where(r => r.TaskId == taskId && (r.Status == RunStatus.Queued || r.Status == RunStatus.Running))
            .ToListAsync(ct);
        active.AddRange(db.ChangeTracker.Entries<TaskRun>()
            .Where(e => e.State == EntityState.Added && e.Entity.TaskId == taskId)
            .Select(e => e.Entity));

        var now = DateTimeOffset.UtcNow;
        foreach (var run in active.Distinct())
        {
            if (run.Status == RunStatus.Queued)
            {
                run.Status = RunStatus.Cancelled;
                run.FinishedAt = now;
                run.ErrorMessage = "Cancelada antes de iniciar";
            }
            else if (run.Status == RunStatus.Running)
            {
                // El worker se entera en su próximo envío de log y detiene el proceso.
                run.CancelRequested = true;
                if (run.RunnerKind == StageKind.System || run.LastActivityAt < now - RunMaxLogAge)
                {
                    run.Status = RunStatus.Cancelled;
                    run.FinishedAt = now;
                    run.ErrorMessage = "Cancelada";
                }
            }
        }
        return active.Count;
    }

    public void AddEvent(int taskId, string actor, string message) =>
        db.TaskEvents.Add(new TaskEvent { TaskId = taskId, Actor = actor, Message = Truncate(message, 1000) });

    /// <summary>PostgreSQL no admite el carácter NUL en columnas de texto.</summary>
    public static string? Clean(string? text) => text?.Replace("\0", "");

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
