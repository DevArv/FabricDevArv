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
public sealed class WorkflowService(AppDbContext Db, AttachmentStorage Storage, ILogger<WorkflowService> Logger)
{
    private static readonly TimeSpan _RunMaxLogAge = TimeSpan.FromMinutes(5);

    /// <summary>Margen sobre el tiempo máximo de la columna antes de que el servidor dé por colgada una ejecución.</summary>
    private const int TIMEOUT_GRACE_MINUTES = 15;
    private const int DEFAULT_TIMEOUT_MINUTES = 60;

    public Task<TaskItem?> LoadTaskAsync(int TaskID, CancellationToken CT) =>
        Db.Tasks.Include(t => t.Stage).Include(t => t.Project).FirstOrDefaultAsync(t => t.ID == TaskID, CT);

    public async Task<Stage> GetInitialStageAsync(CancellationToken CT) =>
        await Db.Stages.OrderByDescending(s => s.IsInitial).ThenBy(s => s.Order).FirstOrDefaultAsync(CT)
        ?? throw new WorkflowException("No hay columnas configuradas en el flujo.");

    public Task<Stage?> GetStageAsync(int StageID, CancellationToken CT) =>
        Db.Stages.AsNoTracking().FirstOrDefaultAsync(s => s.ID == StageID, CT);

    /// <summary>
    /// Crea una tarea: valida proyecto y adjuntos, la inserta, guarda los archivos y la hace entrar a su primera columna
    /// (o a la primera de desarrollo si <see cref="TaskCreateViewModel.StartNow"/>). Todo ocurre en una transacción:
    /// si algo falla no queda la tarea a medias ni archivos huérfanos. Lanza ValidationFailedException.
    /// </summary>
    public async Task<TaskItem> CreateTaskAsync(
        TaskCreateViewModel Record, IReadOnlyCollection<IFormFile> Files, string Actor, string? UserID, CancellationToken CT)
    {
        var errors = new List<(string Field, string Message)>();
        var project = await Db.Projects.FirstOrDefaultAsync(p => p.ID == Record.ProjectID && p.IsActive, CT);
        if (project is null) errors.Add(("Input.ProjectId", "Selecciona un proyecto activo."));

        List<ValidatedUpload> uploads = [];
        try
        {
            uploads = await Storage.ValidateAsync(
                Files: Files,
                ExistingCount: 0,
                CT: CT);
        }
        catch (AttachmentValidationException ex)
        {
            errors.Add(("Files", ex.Message));
        }
        if (errors.Count > 0 || project is null) throw new ValidationFailedException(errors);

        var initial = await GetInitialStageAsync(CT);
        var preferredWorkerID = await Db.Workers.AnyAsync(w => w.ID == Record.PreferredWorkerID && w.IsEnabled, CT)
            ? Record.PreferredWorkerID
            : null;

        await using var transaction = await Db.Database.BeginTransactionAsync(CT);
        List<TaskAttachment> stored = [];
        try
        {
            var task = new TaskItem
            {
                Title = Record.Title.Trim(),
                Description = Record.Description?.Trim() ?? "",
                ProjectID = project.ID,
                Project = project,
                StageID = initial.ID,
                BaseBranch = Record.BaseBranch.Trim(),
                Priority = Record.Priority,
                Complexity = Record.Complexity,
                Labels = TaskLabelsHelper.Normalize(Record.Labels),
                ModelOverride = string.IsNullOrWhiteSpace(Record.ModelOverride) ? null : Record.ModelOverride.Trim(),
                PreferredWorkerID = preferredWorkerID,
                CreatedByID = UserID,
                CreatedByName = Actor,
            };
            Db.Tasks.Add(task);
            await Db.SaveChangesAsync(CT);

            stored = await Storage.StoreAsync(
                TaskID: task.ID,
                Validated: uploads,
                CT: CT);
            Db.TaskAttachments.AddRange(stored);

            var start = initial;
            if (Record.StartNow)
            {
                start = await Db.Stages.Where(s => s.Kind == StageKind.AGENT && s.PushChanges)
                    .OrderBy(s => s.Order).FirstOrDefaultAsync(CT) ?? initial;
            }
            await EnterStageAsync(
                Item: task,
                Stage: start,
                Actor: Actor,
                Message: $"{Actor} creó la tarea en «{start.Name}»",
                CT: CT);
            await Db.SaveChangesAsync(CT);
            await transaction.CommitAsync(CT);
            return task;
        }
        catch
        {
            Storage.DeleteQuietly(stored);
            throw;
        }
    }

    // ------------------------------------------------------------------ acciones de usuario

    public async Task MoveAsync(TaskItem Item, int StageID, string Actor, CancellationToken CT)
    {
        var target = await Db.Stages.FindAsync([StageID], CT) ?? throw new WorkflowException("La columna no existe.");
        await EnterStageAsync(
            Item: Item,
            Stage: target,
            Actor: Actor,
            Message: $"{Actor} movió la tarea a «{target.Name}»",
            CT: CT);
        await Db.SaveChangesAsync(CT);
    }

    /// <summary>
    /// Arrastrar y soltar una tarjeta del tablero. Soltarla en su misma columna no hace nada
    /// (para re-ejecutar un agente se usa «Ejecutar de nuevo»). Devuelve false si la tarea no existe.
    /// </summary>
    public async Task<bool> DropOnStageAsync(int TaskID, int StageID, string Actor, CancellationToken CT)
    {
        var task = await LoadTaskAsync(
            TaskID: TaskID,
            CT: CT);
        if (task is null) return false;
        if (task.StageID != StageID)
        {
            await MoveAsync(
                Item: task,
                StageID: StageID,
                Actor: Actor,
                CT: CT);
        }
        return true;
    }

    public async Task ApproveAsync(TaskItem Item, string Actor, CancellationToken CT)
    {
        if (Item.Stage.Kind is not (StageKind.HUMAN_APPROVAL or StageKind.MANUAL))
            throw new WorkflowException("Esta columna no se aprueba manualmente.");
        var nextID = Item.Stage.SuccessStageID
            ?? throw new WorkflowException($"«{Item.Stage.Name}» no tiene columna destino configurada.");
        var next = await Db.Stages.FindAsync([nextID], CT) ?? throw new WorkflowException("La columna destino no existe.");
        var verb = Item.Stage.Kind == StageKind.HUMAN_APPROVAL ? "aprobó" : "avanzó";
        await EnterStageAsync(
            Item: Item,
            Stage: next,
            Actor: Actor,
            Message: $"{Actor} {verb} en «{Item.Stage.Name}» → «{next.Name}»",
            CT: CT);
        await Db.SaveChangesAsync(CT);
    }

    public async Task RequestChangesAsync(TaskItem Item, string Actor, string Feedback, CancellationToken CT)
    {
        if (string.IsNullOrWhiteSpace(Feedback))
            throw new WorkflowException("Describe los cambios que necesitas.");

        var targetID = Item.Stage.RejectStageID
            ?? await Db.Stages.Where(s => s.Kind == StageKind.AGENT && s.PushChanges)
                .OrderBy(s => s.Order).Select(s => (int?)s.ID).FirstOrDefaultAsync(CT)
            ?? throw new WorkflowException("No hay columna destino para pedir cambios.");
        var target = await Db.Stages.FindAsync([targetID], CT) ?? throw new WorkflowException("La columna destino no existe.");

        Db.TaskComments.Add(new TaskComment
        {
            TaskID = Item.ID, Kind = CommentKind.FEEDBACK, Author = Actor, Body = Feedback.Trim(),
        });
        await EnterStageAsync(
            Item: Item,
            Stage: target,
            Actor: Actor,
            Message: $"{Actor} pidió cambios → «{target.Name}»",
            CT: CT);
        await Db.SaveChangesAsync(CT);
    }

    public async Task AddCommentAsync(TaskItem Item, string Actor, string Body, bool AsFeedback, CancellationToken CT)
    {
        if (string.IsNullOrWhiteSpace(Body)) throw new WorkflowException("El comentario está vacío.");
        Db.TaskComments.Add(new TaskComment
        {
            TaskID = Item.ID,
            Kind = AsFeedback ? CommentKind.FEEDBACK : CommentKind.COMMENT,
            Author = Actor,
            Body = Body.Trim(),
        });
        Item.UpdatedAt = DateTimeOffset.UtcNow;
        await Db.SaveChangesAsync(CT);
    }

    public async Task RetryAsync(TaskItem Item, string Actor, CancellationToken CT)
    {
        if (!Item.Stage.RunsAutomatically)
            throw new WorkflowException("Sólo se reintentan columnas de agente o automáticas.");
        // Reintentar un arreglo de conflictos que falló vuelve a integrar la rama base, mientras sigan los conflictos.
        var resolveConflicts = Item.PullRequestHasConflicts
                               && await Db.TaskRuns.Where(r => r.TaskID == Item.ID && r.StageID == Item.StageID)
                                   .OrderByDescending(r => r.ID).Select(r => r.ResolveConflicts).FirstOrDefaultAsync(CT);
        await EnterStageAsync(
            Item: Item,
            Stage: Item.Stage,
            Actor: Actor,
            Message: $"{Actor} reintentó «{Item.Stage.Name}»",
            CT: CT,
            ResolveConflicts: resolveConflicts);
        await Db.SaveChangesAsync(CT);
    }

    public async Task CancelAsync(TaskItem Item, string Actor, CancellationToken CT)
    {
        var cancelled = await CancelActiveRunsAsync(
            TaskID: Item.ID,
            CT: CT);
        if (cancelled == 0) throw new WorkflowException("No hay ejecuciones activas.");
        Item.Status = WorkStatus.IDLE;
        Item.LastError = $"Cancelado por {Actor}";
        Item.UpdatedAt = DateTimeOffset.UtcNow;
        AddEvent(
            TaskID: Item.ID,
            Actor: Actor,
            Message: $"{Actor} canceló la ejecución en «{Item.Stage.Name}»");
        await Db.SaveChangesAsync(CT);
    }

    public async Task SetArchivedAsync(TaskItem Item, bool Archived, string Actor, CancellationToken CT)
    {
        if (Archived)
        {
            await CancelActiveRunsAsync(
                TaskID: Item.ID,
                CT: CT);
        }
        Item.ArchivedAt = Archived ? DateTimeOffset.UtcNow : null;
        AddEvent(
            TaskID: Item.ID,
            Actor: Actor,
            Message: Archived ? $"{Actor} archivó la tarea" : $"{Actor} restauró la tarea");
        await Db.SaveChangesAsync(CT);
    }

    // ------------------------------------------------------------------ merge y conflictos del pull request

    /// <summary>
    /// Pull requests a vigilar (merge y conflictos): tareas activas con PR aún no integrado que están antes de la columna
    /// destino del merge (<see cref="Stage.IsMergeDestination"/>) o, si ninguna está marcada, en una columna no final.
    /// </summary>
    public async Task<List<WatchedPullRequest>> GetPullRequestsToWatchAsync(CancellationToken CT)
    {
        var destination = await GetMergeDestinationAsync(CT);

        var tasks = await Db.Tasks.AsNoTracking()
            .Include(t => t.Stage).Include(t => t.Project)
            .Where(t => t.ArchivedAt == null && t.PullRequestNumber != null && t.PullRequestMergedAt == null
                        && t.Project.IsActive)
            .ToListAsync(CT);

        return tasks
            .Where(t => PullRequestConflictRule.ShouldWatch(t.Stage, destination))
            .Select(t => new WatchedPullRequest(t.ID, t.Project.RepoOwner, t.Project.RepoName, t.PullRequestNumber!.Value,
                t.PullRequestHasConflicts))
            .ToList();
    }

    /// <summary>
    /// Registra lo que GitHub informa sobre los conflictos del pull request <paramref name="Number"/> de la tarea y deja
    /// constancia en el historial cuando cambia. No hace nada si la tarea cambió de pull request o ya se integró.
    /// </summary>
    public async Task OnPullRequestConflictsCheckedAsync(int TaskID, int Number, bool HasConflicts, string Actor, CancellationToken CT)
    {
        var task = await Db.Tasks.FirstOrDefaultAsync(t => t.ID == TaskID, CT);
        if (task is null || task.PullRequestNumber != Number || task.PullRequestMergedAt is not null
            || task.PullRequestHasConflicts == HasConflicts) return;

        task.PullRequestHasConflicts = HasConflicts;
        task.UpdatedAt = DateTimeOffset.UtcNow;
        AddEvent(
            TaskID: task.ID,
            Actor: Actor,
            Message: HasConflicts
                ? $"El pull request #{Number} tiene conflictos con «{task.BaseBranch}» en GitHub"
                : $"El pull request #{Number} ya no tiene conflictos");
        await Db.SaveChangesAsync(CT);
    }

    /// <summary>
    /// Botón «Arreglar conflictos»: regresa la tarea a la columna del desarrollador (primera columna de agente que hace
    /// push) con una ejecución que integra la rama base en la rama de trabajo y pide al agente resolver los conflictos.
    /// Lanza WorkflowException si el pull request no tiene conflictos o la tarea ya se está ejecutando.
    /// </summary>
    public async Task ResolveConflictsAsync(TaskItem Item, string Actor, CancellationToken CT)
    {
        if (Item.ArchivedAt is not null) throw new WorkflowException("La tarea está archivada.");
        if (Item.PullRequestNumber is not { } number || Item.PullRequestMergedAt is not null)
            throw new WorkflowException("La tarea no tiene un pull request abierto.");
        if (!Item.PullRequestHasConflicts)
            throw new WorkflowException($"GitHub no informa conflictos en el pull request #{number}.");
        if (Item.Status is WorkStatus.QUEUED or WorkStatus.RUNNING)
            throw new WorkflowException($"La tarea ya se está ejecutando en «{Item.Stage.Name}». Espera a que termine o cancélala.");
        if (!PullRequestConflictRule.CanResolve(Item, Item.Stage))
            throw new WorkflowException($"No se pueden arreglar conflictos de una tarea en «{Item.Stage.Name}».");

        var developer = await Db.Stages.Where(s => s.Kind == StageKind.AGENT && s.PushChanges)
                            .OrderBy(s => s.Order).FirstOrDefaultAsync(CT)
                        ?? throw new WorkflowException("No hay columna de desarrollador (agente que hace push) en el flujo.");

        await EnterStageAsync(
            Item: Item,
            Stage: developer,
            Actor: Actor,
            Message: $"{Actor} pidió arreglar los conflictos del pull request #{number} → «{developer.Name}»",
            CT: CT,
            ResolveConflicts: true);
        await Db.SaveChangesAsync(CT);
    }

    /// <summary>
    /// Registra que el pull request <paramref name="Number"/> de la tarea se integró y, si la tarea sigue antes de la
    /// columna destino del merge, la hace entrar a ella. No hace nada si la tarea ya cambió de pull request.
    /// </summary>
    public async Task OnPullRequestMergedAsync(int TaskID, int Number, string Actor, CancellationToken CT)
    {
        var task = await LoadTaskAsync(
            TaskID: TaskID,
            CT: CT);
        if (task is null || task.PullRequestNumber != Number || task.PullRequestMergedAt is not null) return;

        task.PullRequestMergedAt = DateTimeOffset.UtcNow;
        task.PullRequestHasConflicts = false;
        var destination = await GetMergeDestinationAsync(CT);
        if (destination is not null && task.ArchivedAt is null && MergedPullRequestRule.ShouldMove(task.Stage, destination))
        {
            await EnterStageAsync(
                Item: task,
                Stage: destination,
                Actor: Actor,
                Message: $"Se hizo merge del pull request #{Number} en GitHub → «{destination.Name}»",
                CT: CT);
        }
        else
        {
            task.UpdatedAt = DateTimeOffset.UtcNow;
            AddEvent(
                TaskID: task.ID,
                Actor: Actor,
                Message: $"Se hizo merge del pull request #{Number} en GitHub");
        }
        await Db.SaveChangesAsync(CT);
    }

    private Task<Stage?> GetMergeDestinationAsync(CancellationToken CT) =>
        Db.Stages.Where(s => s.IsMergeDestination).OrderBy(s => s.Order).FirstOrDefaultAsync(CT);

    // ------------------------------------------------------------------ motor

    /// <summary>Coloca la tarea en una columna y, si es automática, encola una ejecución. No guarda.</summary>
    /// <param name="ResolveConflicts">
    /// La ejecución de agente encolada es para arreglar los conflictos del pull request (<see cref="TaskRun.ResolveConflicts"/>).
    /// </param>
    public async Task EnterStageAsync(
        TaskItem Item, Stage Stage, string Actor, string Message, CancellationToken CT, bool ResolveConflicts = false)
    {
        await CancelActiveRunsAsync(
            TaskID: Item.ID,
            CT: CT);

        if (Item.Project is null) await Db.Entry(Item).Reference(t => t.Project).LoadAsync(CT);

        var now = DateTimeOffset.UtcNow;
        Item.StageID = Stage.ID;
        Item.Stage = Stage;
        Item.StageEnteredAt = now;
        Item.UpdatedAt = now;
        Item.LastError = null;

        switch (Stage.Kind)
        {
            case StageKind.AGENT:
            case StageKind.SYSTEM:
                if (string.IsNullOrEmpty(Item.WorkBranch))
                    Item.WorkBranch = $"{Item.Project!.BranchPrefix}{Item.ID}-{Slug.Create(Item.Title)}";
                Db.TaskRuns.Add(new TaskRun
                {
                    TaskID = Item.ID,
                    StageID = Stage.ID,
                    StageName = Stage.Name,
                    RunnerKind = Stage.Kind,
                    Status = RunStatus.QUEUED,
                    PinnedWorkerID = Stage.Kind == StageKind.AGENT ? Item.PreferredWorkerID : null,
                    ResolveConflicts = ResolveConflicts && Stage.Kind == StageKind.AGENT && Stage.PushChanges,
                    Model = Stage.Kind == StageKind.AGENT ? (Item.ModelOverride ?? Stage.Model) : null,
                    CreatedAt = now,
                });
                Item.Status = WorkStatus.QUEUED;
                break;
            case StageKind.HUMAN_APPROVAL:
                Item.Status = WorkStatus.WAITING_APPROVAL;
                break;
            case StageKind.TERMINAL:
                Item.Status = WorkStatus.DONE;
                break;
            default:
                Item.Status = WorkStatus.IDLE;
                break;
        }

        AddEvent(
            TaskID: Item.ID,
            Actor: Actor,
            Message: Message);
    }

    /// <summary>Un worker reclama la siguiente ejecución de agente en cola (seguro ante concurrencia).</summary>
    public async Task<TaskRun?> ClaimNextAgentRunAsync(WorkerNode Worker, CancellationToken CT)
    {
        var candidates = await Db.TaskRuns
            .Where(r => r.Status == RunStatus.QUEUED && r.RunnerKind == StageKind.AGENT
                        && (r.PinnedWorkerID == null || r.PinnedWorkerID == Worker.ID)
                        && r.Task.ArchivedAt == null && r.Task.Project.IsActive)
            .OrderBy(r => r.CreatedAt)
            .Select(r => r.ID)
            .Take(5)
            .ToListAsync(CT);

        foreach (var runID in candidates)
        {
            if (await TryMarkRunningAsync(
                    RunID: runID,
                    WorkerID: Worker.ID,
                    CT: CT))
            {
                return await OnRunStartedAsync(
                    RunID: runID,
                    Actor: Worker.Name,
                    CT: CT);
            }
        }
        return null;
    }

    /// <summary>El orquestador del servidor reclama la siguiente ejecución de sistema (crear PR, merge).</summary>
    public async Task<TaskRun?> ClaimNextSystemRunAsync(CancellationToken CT)
    {
        var candidates = await Db.TaskRuns
            .Where(r => r.Status == RunStatus.QUEUED && r.RunnerKind == StageKind.SYSTEM && r.Task.ArchivedAt == null)
            .OrderBy(r => r.CreatedAt)
            .Select(r => r.ID)
            .Take(5)
            .ToListAsync(CT);

        foreach (var runID in candidates)
        {
            if (await TryMarkRunningAsync(
                    RunID: runID,
                    WorkerID: null,
                    CT: CT))
            {
                return await OnRunStartedAsync(
                    RunID: runID,
                    Actor: "Sistema",
                    CT: CT);
            }
        }
        return null;
    }

    /// <summary>Guarda el prompt final que se le entregó al agente en la ejecución, para poder auditarlo.</summary>
    public async Task SavePromptSnapshotAsync(TaskRun Run, string Prompt, CancellationToken CT)
    {
        Run.PromptSnapshot = Prompt;
        await Db.SaveChangesAsync(CT);
    }

    private async Task<bool> TryMarkRunningAsync(long RunID, int? WorkerID, CancellationToken CT)
    {
        DateTimeOffset? now = DateTimeOffset.UtcNow;
        var updated = await Db.TaskRuns
            .Where(r => r.ID == RunID && r.Status == RunStatus.QUEUED)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, RunStatus.RUNNING)
                .SetProperty(r => r.WorkerID, WorkerID)
                .SetProperty(r => r.StartedAt, now)
                .SetProperty(r => r.LastActivityAt, now), CT);
        return updated == 1;
    }

    private async Task<TaskRun> OnRunStartedAsync(long RunID, string Actor, CancellationToken CT)
    {
        var run = await Db.TaskRuns
            .Include(r => r.Task).ThenInclude(t => t.Project)
            .Include(r => r.Task).ThenInclude(t => t.Stage)
            .Include(r => r.Task).ThenInclude(t => t.Attachments)
            .Include(r => r.Task).ThenInclude(t => t.Comments)
            .AsSplitQuery()
            .FirstAsync(r => r.ID == RunID, CT);

        run.Task.Status = WorkStatus.RUNNING;
        run.Task.UpdatedAt = DateTimeOffset.UtcNow;
        if (run.RunnerKind == StageKind.AGENT) run.Task.LastWorkerName = Actor;
        AddEvent(
            TaskID: run.TaskID,
            Actor: Actor,
            Message: $"{Actor} tomó el pedido «{run.StageName}»");
        await Db.SaveChangesAsync(CT);
        return run;
    }

    /// <summary>Registra el resultado de una ejecución y avanza (o devuelve) la tarea según la columna.</summary>
    public async Task CompleteRunAsync(long RunID, RunCompletion Completion, string Actor, CancellationToken CT)
    {
        var run = await Db.TaskRuns
            .Include(r => r.Task).ThenInclude(t => t.Stage)
            .Include(r => r.Task).ThenInclude(t => t.Project)
            .FirstOrDefaultAsync(r => r.ID == RunID, CT);
        if (run is null || run.Status != RunStatus.RUNNING)
        {
            Logger.LogWarning("Resultado ignorado para la ejecución {RunId}: no está en curso.", RunID);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var task = run.Task;
        var changedFiles = Completion.ChangedFiles ?? [];
        run.FinishedAt = now;
        run.LastActivityAt = now;
        run.CostUsd = Completion.CostUsd;
        run.InputTokens = Completion.InputTokens;
        run.OutputTokens = Completion.OutputTokens;
        run.CacheReadTokens = Completion.CacheReadTokens;
        run.CacheWriteTokens = Completion.CacheWriteTokens;
        run.NumTurns = Completion.NumTurns;
        run.SessionID = Completion.SessionID;
        run.Model = Completion.Model ?? run.Model;
        run.HeadCommit = Completion.HeadCommit;
        run.Summary = Clean(Completion.ResultText);
        run.ChangedFilesJson = changedFiles.Count > 0 ? JsonSerializer.Serialize(changedFiles) : null;

        task.TotalCostUsd += Completion.CostUsd;
        task.TotalTokens += Completion.InputTokens + Completion.OutputTokens + Completion.CacheReadTokens + Completion.CacheWriteTokens;
        task.UpdatedAt = now;

        // Sólo la ejecución más reciente de la columna actual puede mover la tarea.
        var isCurrent = task.StageID == run.StageID
                        && !await Db.TaskRuns.AnyAsync(r => r.TaskID == task.ID && r.ID > run.ID, CT);

        if (Completion.Cancelled || run.CancelRequested)
        {
            run.Status = RunStatus.CANCELLED;
            run.ErrorMessage = Clean(Completion.ErrorMessage) ?? "Ejecución cancelada";
            if (isCurrent && task.Status is WorkStatus.RUNNING or WorkStatus.QUEUED)
            {
                task.Status = WorkStatus.IDLE;
                task.LastError = "Ejecución cancelada";
            }
            AddEvent(
                TaskID: task.ID,
                Actor: Actor,
                Message: $"Se canceló «{run.StageName}»");
            await Db.SaveChangesAsync(CT);
            return;
        }

        if (!Completion.Success)
        {
            await FailAsync(
                Run: run,
                Item: task,
                IsCurrent: isCurrent,
                Error: Clean(Completion.ErrorMessage) ?? "La ejecución falló sin detalle.",
                Actor: Actor,
                CT: CT);
            return;
        }

        var stage = task.Stage;
        if (isCurrent && stage.PushChanges && changedFiles.Count == 0)
        {
            await FailAsync(
                Run: run,
                Item: task,
                IsCurrent: isCurrent,
                Error: "El agente terminó sin cambios en la rama respecto a la rama base.",
                Actor: Actor,
                CT: CT);
            return;
        }

        run.Status = RunStatus.SUCCEEDED;
        if (!isCurrent)
        {
            await Db.SaveChangesAsync(CT);
            return;
        }

        if (stage.RequiresDecision)
        {
            run.Decision = DecisionParser.Parse(Completion.ResultText);
            switch (run.Decision)
            {
                case RunDecision.NONE:
                    task.Status = WorkStatus.FAILED;
                    task.LastError = "El agente no emitió veredicto («VEREDICTO: APROBADO» o «VEREDICTO: RECHAZADO»).";
                    AddEvent(
                        TaskID: task.ID,
                        Actor: Actor,
                        Message: $"«{stage.Name}» terminó sin veredicto");
                    await Db.SaveChangesAsync(CT);
                    return;

                case RunDecision.REJECTED:
                    Db.TaskComments.Add(new TaskComment
                    {
                        TaskID = task.ID, Kind = CommentKind.REVIEWER_NOTE, Author = stage.Name,
                        Body = run.Summary ?? "", RunID = run.ID,
                    });
                    await GoToAsync(
                        Item: task,
                        StageID: stage.RejectStageID,
                        Actor: Actor,
                        Reason: $"«{stage.Name}» rechazó",
                        CT: CT);
                    await Db.SaveChangesAsync(CT);
                    return;
            }
        }

        var verb = run.Decision == RunDecision.APPROVED ? "aprobó" : "completó";
        await GoToAsync(
            Item: task,
            StageID: stage.SuccessStageID,
            Actor: Actor,
            Reason: $"«{stage.Name}» {verb}",
            CT: CT);
        await Db.SaveChangesAsync(CT);
    }

    /// <summary>
    /// Marca como fallidas las ejecuciones de agente en curso cuya PC dejó de responder (sin actividad ni latido durante
    /// <paramref name="WorkerSilence"/>) o que excedieron el tiempo máximo de su columna. Devuelve cuántas marcó.
    /// </summary>
    public async Task<int> FailStalledRunsAsync(TimeSpan WorkerSilence, CancellationToken CT)
    {
        var now = DateTimeOffset.UtcNow;

        var running = await Db.TaskRuns.AsNoTracking()
            .Where(r => r.Status == RunStatus.RUNNING && r.RunnerKind == StageKind.AGENT)
            .Select(r => new
            {
                r.ID,
                r.StartedAt,
                r.LastActivityAt,
                WorkerSeen = r.Worker != null ? r.Worker.LastSeenAt : null,
                Timeout = Db.Stages.Where(s => s.ID == r.StageID).Select(s => (int?)s.TimeoutMinutes).FirstOrDefault(),
            })
            .ToListAsync(CT);

        var failed = 0;
        foreach (var run in running)
        {
            string? reason = null;
            var silentSince = run.LastActivityAt ?? run.StartedAt ?? now;
            if (now - silentSince > WorkerSilence && (run.WorkerSeen is null || now - run.WorkerSeen > WorkerSilence))
                reason = "El worker dejó de responder durante la ejecución.";
            else if (run.StartedAt is { } started
                     && now - started > TimeSpan.FromMinutes((run.Timeout ?? DEFAULT_TIMEOUT_MINUTES) + TIMEOUT_GRACE_MINUTES))
                reason = "La ejecución excedió el tiempo máximo de la columna.";

            if (reason is null) continue;
            Logger.LogWarning("Ejecución {RunId}: {Reason}", run.ID, reason);
            await CompleteRunAsync(
                RunID: run.ID,
                Completion: SystemActionExecutor.Failure(reason),
                Actor: "Vigilante",
                CT: CT);
            failed++;
        }
        return failed;
    }

    private async Task GoToAsync(TaskItem Item, int? StageID, string Actor, string Reason, CancellationToken CT)
    {
        var next = StageID is null ? null : await Db.Stages.FindAsync([StageID.Value], CT);
        if (next is null)
        {
            Item.Status = WorkStatus.DONE;
            AddEvent(
                TaskID: Item.ID,
                Actor: Actor,
                Message: $"{Reason}; la columna no tiene destino configurado");
            return;
        }
        await EnterStageAsync(
            Item: Item,
            Stage: next,
            Actor: Actor,
            Message: $"{Reason} → «{next.Name}»",
            CT: CT);
    }

    private async Task FailAsync(TaskRun Run, TaskItem Item, bool IsCurrent, string Error, string Actor, CancellationToken CT)
    {
        Run.Status = RunStatus.FAILED;
        Run.ErrorMessage = Error;
        if (IsCurrent)
        {
            Item.Status = WorkStatus.FAILED;
            Item.LastError = Error;
        }
        AddEvent(
            TaskID: Item.ID,
            Actor: Actor,
            Message: $"«{Run.StageName}» falló: {Truncate(Error, 300)}");
        await Db.SaveChangesAsync(CT);
    }

    private async Task<int> CancelActiveRunsAsync(int TaskID, CancellationToken CT)
    {
        // Incluye las ejecuciones ya agregadas al contexto y aún no guardadas.
        var active = await Db.TaskRuns
            .Where(r => r.TaskID == TaskID && (r.Status == RunStatus.QUEUED || r.Status == RunStatus.RUNNING))
            .ToListAsync(CT);
        active.AddRange(Db.ChangeTracker.Entries<TaskRun>()
            .Where(e => e.State == EntityState.Added && e.Entity.TaskID == TaskID)
            .Select(e => e.Entity));

        var now = DateTimeOffset.UtcNow;
        foreach (var run in active.Distinct())
        {
            if (run.Status == RunStatus.QUEUED)
            {
                run.Status = RunStatus.CANCELLED;
                run.FinishedAt = now;
                run.ErrorMessage = "Cancelada antes de iniciar";
            }
            else if (run.Status == RunStatus.RUNNING)
            {
                // El worker se entera en su próximo envío de log y detiene el proceso.
                run.CancelRequested = true;
                if (run.RunnerKind == StageKind.SYSTEM || run.LastActivityAt < now - _RunMaxLogAge)
                {
                    run.Status = RunStatus.CANCELLED;
                    run.FinishedAt = now;
                    run.ErrorMessage = "Cancelada";
                }
            }
        }
        return active.Count;
    }

    public void AddEvent(int TaskID, string Actor, string Message) =>
        Db.TaskEvents.Add(new TaskEvent { TaskID = TaskID, Actor = Actor, Message = Truncate(Message, 1000) });

    /// <summary>PostgreSQL no admite el carácter NUL en columnas de texto.</summary>
    public static string? Clean(string? Text) => Text?.Replace("\0", "");

    private static string Truncate(string Text, int Max) => Text.Length <= Max ? Text : Text[..(Max - 1)] + "…";
}
