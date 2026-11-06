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

public class TasksRepository(AppDbContext Db, AttachmentStorage Storage, ILogger<TasksRepository> Logger)
{
    private const int MAX_BOARD_TASKS = 1000;

    /// <summary>
    /// Caracteres de la descripción que se traen para la tarjeta del tablero. La tarjeta muestra un extracto de 140
    /// (ver Labels.Excerpt), que quita símbolos de Markdown antes de cortar, así que se deja margen.
    /// </summary>
    private const int MAX_CARD_DESCRIPTION_CHARS = 1000;
    private const int MAX_TITLE_LENGTH = 200;
    private const int MAX_DESCRIPTION_LENGTH = 50_000;
    private const int MAX_MODEL_OVERRIDE_LENGTH = 100;

    public Task<int> CountWaitingAsync(CancellationToken CT) =>
        Db.Tasks.CountAsync(t => t.ArchivedAt == null && t.Status == WorkStatus.WaitingApproval, CT);

    /// <summary>
    /// Tareas que el agente desarrollador (columna de agente que hace commit y push) está ejecutando ahora,
    /// en todos los proyectos.
    /// </summary>
    public Task<int> CountDevelopingAsync(CancellationToken CT) =>
        Db.Tasks.CountAsync(t => t.ArchivedAt == null
                                 && t.Status == WorkStatus.Running
                                 && t.Stage.Kind == StageKind.Agent
                                 && t.Stage.PushChanges, CT);

    public async Task<BoardViewModel> GetBoardAsync(BoardFilterViewModel Filter, CancellationToken CT)
    {
        var stages = await Db.Stages.AsNoTracking().OrderBy(s => s.Order).ToListAsync(CT);
        var names = stages.ToDictionary(s => s.Id, s => s.Name);

        var query = Db.Tasks.AsNoTracking()
            .Where(t => Filter.Archived ? t.ArchivedAt != null : t.ArchivedAt == null);
        if (Filter.ProjectID is { } projectID) query = query.Where(t => t.ProjectId == projectID);
        if (!string.IsNullOrWhiteSpace(Filter.WorkerName)) query = query.Where(t => t.LastWorkerName == Filter.WorkerName);
        if (Filter.Waiting) query = query.Where(t => t.Status == WorkStatus.WaitingApproval);
        if (!string.IsNullOrWhiteSpace(Filter.Query))
        {
            var term = Filter.Query.Trim().TrimStart('#');
            var like = BuildLikePattern(term);
            query = int.TryParse(term, out var taskID)
                ? query.Where(t => t.Id == taskID || EF.Functions.ILike(t.Title, like))
                : query.Where(t => EF.Functions.ILike(t.Title, like) || EF.Functions.ILike(t.Description, like));
        }

        var tasks = await query
            .OrderByDescending(t => t.Priority)
            .ThenBy(t => t.CreatedAt)
            .Take(MAX_BOARD_TASKS)
            .Select(t => new TaskItem
            {
                Id = t.Id,
                Title = t.Title,
                Description = t.Description.Length > MAX_CARD_DESCRIPTION_CHARS
                    ? t.Description.Substring(0, MAX_CARD_DESCRIPTION_CHARS)
                    : t.Description,
                ProjectId = t.ProjectId,
                Project = t.Project,
                StageId = t.StageId,
                Status = t.Status,
                Priority = t.Priority,
                Complexity = t.Complexity,
                Labels = t.Labels,
                BaseBranch = t.BaseBranch,
                WorkBranch = t.WorkBranch,
                PullRequestNumber = t.PullRequestNumber,
                PullRequestUrl = t.PullRequestUrl,
                PullRequestMergedAt = t.PullRequestMergedAt,
                LastWorkerName = t.LastWorkerName,
                LastError = t.LastError,
                CreatedByName = t.CreatedByName,
                CreatedAt = t.CreatedAt,
                UpdatedAt = t.UpdatedAt,
                StageEnteredAt = t.StageEnteredAt,
                ArchivedAt = t.ArchivedAt,
                TotalCostUsd = t.TotalCostUsd,
                TotalTokens = t.TotalTokens,
            })
            .ToListAsync(CT);
        var now = DateTimeOffset.UtcNow;
        var developingSince = await GetDevelopingSinceAsync(
            Stages: stages,
            Tasks: tasks,
            CT: CT);
        var byStage = tasks
            .Select(t => new BoardCardViewModel
            {
                Item = t,
                DevelopmentElapsed = developingSince.TryGetValue(t.Id, out var since) && now > since ? now - since : null,
            })
            .GroupBy(c => c.Item.StageId)
            .ToDictionary(g => g.Key, g => g.ToList());

        return new BoardViewModel
        {
            Compact = Filter.Compact,
            HideEmpty = Filter.HideEmpty,
            GeneratedAt = now,
            DevelopingCount = await CountDevelopingAsync(CT),
            Columns = stages.Select(s => new BoardColumnViewModel
            {
                Stage = s,
                Tasks = byStage.GetValueOrDefault(s.Id) ?? [],
                SuccessName = s.SuccessStageId is { } ok ? names.GetValueOrDefault(ok) : null,
                RejectName = s.RejectStageId is { } ko ? names.GetValueOrDefault(ko) : null,
            }).ToList(),
        };
    }

    /// <summary>Patrón de <c>ILIKE</c> «contiene» para <paramref name="Term"/>, con <c>\</c>, <c>%</c> y <c>_</c> escapados.</summary>
    internal static string BuildLikePattern(string Term) =>
        "%" + Term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

    /// <summary>
    /// Para las tareas que el agente desarrollador (columna de agente que hace commit y push) está implementando
    /// ahora, el momento en que una PC tomó la ejecución en curso de esa columna. Clave: id de la tarea.
    /// </summary>
    private async Task<Dictionary<int, DateTimeOffset>> GetDevelopingSinceAsync(
        List<Stage> Stages, List<TaskItem> Tasks, CancellationToken CT)
    {
        var developerStages = Stages.Where(s => s.Kind == StageKind.Agent && s.PushChanges).Select(s => s.Id).ToHashSet();
        var running = Tasks.Where(t => t.Status == WorkStatus.Running && developerStages.Contains(t.StageId))
            .ToDictionary(t => t.Id, t => t.StageId);
        if (running.Count == 0) return new Dictionary<int, DateTimeOffset>();

        var ids = running.Keys.ToList();
        var runs = await Db.TaskRuns.AsNoTracking()
            .Where(r => ids.Contains(r.TaskId) && r.Status == RunStatus.Running && r.StartedAt != null)
            .Select(r => new { r.TaskId, r.StageId, StartedAt = r.StartedAt!.Value })
            .ToListAsync(CT);

        return runs.Where(r => running[r.TaskId] == r.StageId)
            .GroupBy(r => r.TaskId)
            .ToDictionary(g => g.Key, g => g.Max(r => r.StartedAt));
    }

    /// <summary>
    /// Formulario de nueva tarea con el proyecto activo indicado (o el primero por nombre) y su rama por defecto.
    /// Devuelve null si no hay proyectos activos.
    /// </summary>
    public async Task<TaskCreateViewModel?> NewCreateAsync(int? ProjectID, CancellationToken CT)
    {
        var active = Db.Projects.AsNoTracking().Where(p => p.IsActive);
        var project = (ProjectID is null ? null : await active.FirstOrDefaultAsync(p => p.Id == ProjectID, CT))
                      ?? await active.OrderBy(p => p.Name).FirstOrDefaultAsync(CT);
        return project is null
            ? null
            : new TaskCreateViewModel { ProjectId = project.Id, BaseBranch = project.DefaultBranch };
    }

    /// <summary>
    /// Pantalla de detalle: la tarea, las columnas, las PCs habilitadas y la ejecución elegida
    /// (<paramref name="RunID"/> o la más reciente) con sus archivos cambiados. Devuelve null si la tarea no existe.
    /// </summary>
    public async Task<TaskDetailsViewModel?> GetDetailsViewAsync(int ID, long? RunID, CancellationToken CT)
    {
        var task = await GetDetailsAsync(ID, CT);
        if (task is null) return null;

        var selectedRun = task.Runs.FirstOrDefault(r => r.Id == RunID) ?? task.Runs.FirstOrDefault();
        return new TaskDetailsViewModel
        {
            Item = task,
            Stages = await Db.Stages.AsNoTracking().OrderBy(s => s.Order).ToListAsync(CT),
            Workers = await Db.Workers.AsNoTracking().Where(w => w.IsEnabled).OrderBy(w => w.Name).ToListAsync(CT),
            SelectedRun = selectedRun,
            SelectedRunFiles = ParseChangedFiles(selectedRun?.ChangedFilesJson),
        };
    }

    private static List<ChangedFile> ParseChangedFiles(string? Json)
    {
        if (Json is null) return [];
        try { return JsonSerializer.Deserialize<List<ChangedFile>>(Json) ?? []; }
        catch (JsonException) { return []; }
    }

    /// <summary>Tarea con todo su detalle (proyecto, columna, adjuntos, comentarios, eventos y ejecuciones más recientes primero).</summary>
    private async Task<TaskItem?> GetDetailsAsync(int ID, CancellationToken CT)
    {
        var task = await Db.Tasks
            .Include(t => t.Project)
            .Include(t => t.Stage)
            .Include(t => t.PreferredWorker)
            .Include(t => t.Attachments)
            .Include(t => t.Comments)
            .Include(t => t.Events)
            .Include(t => t.Runs).ThenInclude(r => r.Worker)
            .AsSplitQuery()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == ID, CT);
        if (task is null) return null;

        task.Runs = task.Runs.OrderByDescending(r => r.Id).ToList();
        return task;
    }

    /// <summary>
    /// Edita los campos de una tarea ya cargada con seguimiento (ver WorkflowService.LoadTaskAsync).
    /// Lanza ValidationFailedException si algún valor excede el límite de su columna o no es válido.
    /// </summary>
    public async Task UpdateAsync(TaskItem Item, TaskUpdateViewModel Record, string Actor, CancellationToken CT)
    {
        var modelOverride = Record.ModelOverride?.Trim();
        if (!string.IsNullOrEmpty(modelOverride))
        {
            if (modelOverride.Length > MAX_MODEL_OVERRIDE_LENGTH)
                throw new ValidationFailedException(nameof(Record.ModelOverride), $"El modelo admite máximo {MAX_MODEL_OVERRIDE_LENGTH} caracteres.");
            if (!Regex.IsMatch(modelOverride, @"^[A-Za-z0-9][A-Za-z0-9.\-_\[\]]*$"))
                throw new ValidationFailedException(nameof(Record.ModelOverride), "Nombre de modelo inválido.");
        }
        if (Record.Description is { Length: > MAX_DESCRIPTION_LENGTH })
            throw new ValidationFailedException(nameof(Record.Description), $"La descripción admite máximo {MAX_DESCRIPTION_LENGTH} caracteres.");
        if (!Enum.IsDefined(Record.Priority))
            throw new ValidationFailedException(nameof(Record.Priority), "Prioridad inválida.");
        if (!Enum.IsDefined(Record.Complexity))
            throw new ValidationFailedException(nameof(Record.Complexity), "Complejidad inválida.");
        if (Record.PreferredWorkerId is not null && !await Db.Workers.AnyAsync(w => w.Id == Record.PreferredWorkerId, CT))
            throw new ValidationFailedException(nameof(Record.PreferredWorkerId), "La PC no existe.");

        if (!string.IsNullOrWhiteSpace(Record.Title))
        {
            var title = Record.Title.Trim();
            Item.Title = title[..Math.Min(title.Length, MAX_TITLE_LENGTH)];
        }
        if (Record.Description is not null) Item.Description = Record.Description.Trim();
        Item.Priority = Record.Priority;
        Item.Complexity = Record.Complexity;
        Item.Labels = TaskLabelsHelper.Normalize(Record.Labels);
        Item.ModelOverride = string.IsNullOrEmpty(modelOverride) ? null : modelOverride;
        Item.PreferredWorkerId = Record.PreferredWorkerId;
        Item.UpdatedAt = DateTimeOffset.UtcNow;
        Db.TaskEvents.Add(new TaskEvent { TaskId = Item.Id, Actor = Actor, Message = $"{Actor} editó la tarea" });
        await Db.SaveChangesAsync(CT);
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
    public async Task DeleteAsync(TaskItem Item, string Actor, CancellationToken CT)
    {
        await using var transaction = await Db.Database.BeginTransactionAsync(CT);

        await LockTaskAndRunsAsync(Item.Id, CT);

        if (await Db.TaskRuns.AnyAsync(r => r.TaskId == Item.Id && r.Status == RunStatus.Running, CT))
            throw new WorkflowException("La tarea se está ejecutando. Cancela la ejecución y espera a que termine antes de eliminarla.");

        var attachments = await Db.TaskAttachments.Where(a => a.TaskId == Item.Id).ToListAsync(CT);
        Db.Tasks.Remove(Item);
        await Db.SaveChangesAsync(CT);
        await transaction.CommitAsync(CT);

        // La tarea ya no existe: un archivo que no se pueda borrar no debe romper la operación ni impedir borrar los demás.
        foreach (var attachment in attachments)
        {
            try
            {
                Storage.Delete(attachment);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Logger.LogWarning(ex, "No se pudo borrar del disco el adjunto {AttachmentId} de la tarea #{TaskId}.", attachment.Id, Item.Id);
            }
        }

        Logger.LogInformation("{Actor} eliminó la tarea #{TaskId} «{Title}».", Actor, Item.Id, Item.Title);
    }

    /// <summary>
    /// Bloquea (FOR UPDATE) la fila de la tarea y las de sus ejecuciones dentro de la transacción actual.
    /// Es virtual sólo para poder probar <see cref="DeleteAsync"/> sin PostgreSQL: el proveedor en memoria no ejecuta SQL.
    /// </summary>
    protected virtual async Task LockTaskAndRunsAsync(int TaskID, CancellationToken CT)
    {
        await Db.Database.SqlQuery<int>($"""SELECT "Id" AS "Value" FROM "Tasks" WHERE "Id" = {TaskID} FOR UPDATE""").ToListAsync(CT);
        await Db.Database.SqlQuery<long>($"""SELECT "Id" AS "Value" FROM "TaskRuns" WHERE "TaskId" = {TaskID} FOR UPDATE""").ToListAsync(CT);
    }
}
