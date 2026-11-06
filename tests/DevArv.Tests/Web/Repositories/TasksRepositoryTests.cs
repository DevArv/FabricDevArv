using DevArv.Web.DatabaseConnection;
using DevArv.Web.DataModels;
using DevArv.Web.Enums.Runs;
using DevArv.Web.Enums.Tasks;
using DevArv.Web.Enums.Workflow;
using DevArv.Web.Repositories;
using DevArv.Web.Services;
using DevArv.Web.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging.Abstractions;

namespace DevArv.Tests.Web.Repositories;

public sealed class TasksRepositoryTests
{
    private readonly string _DatabaseName = Guid.NewGuid().ToString("N");

    [Fact]
    public async Task DeleteAsync_WithRunningRun_ThrowsAndKeepsTaskAndAttachments()
    {
        await SeedTaskAsync(RunStatus.Running);

        await using var db = CreateDbContext();
        var repository = CreateRepository(db);
        var task = await db.Tasks.SingleAsync();

        var ex = await Assert.ThrowsAsync<WorkflowException>(() => repository.DeleteAsync(
            task: task,
            actor: "tester",
            ct: CancellationToken.None));

        Assert.Contains("se está ejecutando", ex.Message);
        await using var check = CreateDbContext();
        Assert.Equal(1, await check.Tasks.CountAsync());
        Assert.Equal(1, await check.TaskAttachments.CountAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData(RunStatus.Queued)]
    [InlineData(RunStatus.Succeeded)]
    [InlineData(RunStatus.Failed)]
    [InlineData(RunStatus.Cancelled)]
    public async Task DeleteAsync_WithoutRunningRun_DeletesTask(RunStatus? runStatus)
    {
        // Los adjuntos se borran en cascada en la base de datos (ver Model_CascadesTaskDeletion...), que el proveedor
        // en memoria no simula para entidades sin cargar.
        await SeedTaskAsync(runStatus);

        await using var db = CreateDbContext();
        var repository = CreateRepository(db);
        var task = await db.Tasks.SingleAsync();

        await repository.DeleteAsync(
            task: task,
            actor: "tester",
            ct: CancellationToken.None);

        await using var check = CreateDbContext();
        Assert.Equal(0, await check.Tasks.CountAsync());
    }

    [Fact]
    public async Task DeleteAsync_WithRunningRunOfAnotherTask_DeletesTask()
    {
        await SeedTaskAsync(runStatus: null);
        await using (var seed = CreateDbContext())
        {
            var other = NewTask(title: "Otra");
            seed.Tasks.Add(other);
            await seed.SaveChangesAsync();
            seed.TaskRuns.Add(new TaskRun { TaskId = other.Id, StageId = other.StageId, Status = RunStatus.Running });
            await seed.SaveChangesAsync();
        }

        await using var db = CreateDbContext();
        var repository = CreateRepository(db);
        var task = await db.Tasks.SingleAsync(t => t.Title == "Tarea");

        await repository.DeleteAsync(
            task: task,
            actor: "tester",
            ct: CancellationToken.None);

        await using var check = CreateDbContext();
        Assert.Equal(["Otra"], await check.Tasks.Select(t => t.Title).ToListAsync());
    }

    [Fact]
    public void Model_CascadesTaskDeletionToRunsCommentsEventsAndAttachments()
    {
        using var db = CreateDbContext();
        foreach (var entityType in new[] { typeof(TaskRun), typeof(TaskComment), typeof(TaskEvent), typeof(TaskAttachment) })
        {
            var foreignKey = db.Model.FindEntityType(entityType)!.GetForeignKeys().Single(f => f.PrincipalEntityType.ClrType == typeof(TaskItem));
            Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
        }
    }

    [Fact]
    public void Model_StoresAttachmentContentInTaskAttachmentsTable()
    {
        // El contenido va en la columna TaskAttachments.Content (table splitting), no en disco ni en otra tabla.
        using var db = CreateDbContext();
        var model = db.GetService<IDesignTimeModel>().Model;
        var attachment = model.FindEntityType(typeof(TaskAttachment))!;
        var content = model.FindEntityType(typeof(TaskAttachmentContent))!;

        Assert.Equal("TaskAttachments", attachment.GetTableName());
        Assert.Equal("TaskAttachments", content.GetTableName());
        Assert.Equal("Content", content.FindProperty(nameof(TaskAttachmentContent.Data))!.GetColumnName());
        Assert.Null(attachment.FindProperty("StoredName"));
        var foreignKey = content.GetForeignKeys().Single();
        Assert.Equal(typeof(TaskAttachment), foreignKey.PrincipalEntityType.ClrType);
        Assert.True(foreignKey.IsUnique);
    }

    // ------------------------------------------------------------------ conflictos del pull request

    private const int APPROVAL_STAGE_ID = 1;
    private const int DEVELOPER_STAGE_ID = 2;
    private const int TERMINAL_STAGE_ID = 3;

    [Theory]
    [InlineData(RunStatus.Queued, true)]
    [InlineData(RunStatus.Running, true)]
    [InlineData(RunStatus.Succeeded, false)]
    [InlineData(RunStatus.Failed, false)]
    [InlineData(RunStatus.Cancelled, false)]
    public async Task GetBoardAsync_ResolvingConflicts_OnlyWithActiveResolveConflictsRun(RunStatus runStatus, bool expected)
    {
        await SeedStagesAsync();
        var taskId = await SeedConflictTaskAsync(
            stageId: DEVELOPER_STAGE_ID,
            status: WorkStatus.Running,
            runs: [(runStatus, resolveConflicts: true)]);

        var card = await GetBoardCardAsync(taskId);

        Assert.Equal(expected, card.ResolvingConflicts);
    }

    [Fact]
    public async Task GetBoardAsync_ActiveRunWithoutResolveConflicts_IsNotResolvingConflicts()
    {
        await SeedStagesAsync();
        var taskId = await SeedConflictTaskAsync(
            stageId: DEVELOPER_STAGE_ID,
            status: WorkStatus.Running,
            runs: [(RunStatus.Running, resolveConflicts: false)]);

        var card = await GetBoardCardAsync(taskId);

        Assert.False(card.ResolvingConflicts);
    }

    [Fact]
    public async Task GetBoardAsync_ResolveConflictsRunOfAnotherTask_DoesNotMarkTask()
    {
        await SeedStagesAsync();
        var taskId = await SeedConflictTaskAsync(
            stageId: DEVELOPER_STAGE_ID,
            status: WorkStatus.Running,
            runs: [(RunStatus.Running, resolveConflicts: false)]);
        var otherId = await SeedConflictTaskAsync(
            stageId: DEVELOPER_STAGE_ID,
            status: WorkStatus.Queued,
            runs: [(RunStatus.Queued, resolveConflicts: true)]);

        await using var db = CreateDbContext();
        var board = await CreateRepository(db).GetBoardAsync(new BoardFilterViewModel(), CancellationToken.None);
        var cards = board.Columns.SelectMany(c => c.Tasks).ToDictionary(c => c.Item.Id);

        Assert.False(cards[taskId].ResolvingConflicts);
        Assert.True(cards[otherId].ResolvingConflicts);
    }

    [Fact]
    public async Task GetBoardAsync_IdleTaskWithLeftoverActiveRun_IsNotResolvingConflicts()
    {
        // Sólo se consideran las tareas en cola o en ejecución.
        await SeedStagesAsync();
        var taskId = await SeedConflictTaskAsync(
            stageId: DEVELOPER_STAGE_ID,
            status: WorkStatus.Idle,
            runs: [(RunStatus.Queued, resolveConflicts: true)]);

        var card = await GetBoardCardAsync(taskId);

        Assert.False(card.ResolvingConflicts);
    }

    [Theory]
    // Aprobación Producto con conflictos: se muestra el estado y el botón 🔧.
    [InlineData(APPROVAL_STAGE_ID, WorkStatus.WaitingApproval, true, true, false, true, true)]
    [InlineData(APPROVAL_STAGE_ID, WorkStatus.Idle, true, true, false, true, true)]
    // Sin conflictos, sin pull request o ya integrado: nada.
    [InlineData(APPROVAL_STAGE_ID, WorkStatus.WaitingApproval, false, true, false, false, false)]
    [InlineData(APPROVAL_STAGE_ID, WorkStatus.WaitingApproval, true, false, false, false, false)]
    [InlineData(APPROVAL_STAGE_ID, WorkStatus.WaitingApproval, true, true, true, false, false)]
    // En ejecución o en una columna final: se muestra el estado, pero no el botón.
    [InlineData(DEVELOPER_STAGE_ID, WorkStatus.Running, true, true, false, true, false)]
    [InlineData(DEVELOPER_STAGE_ID, WorkStatus.Queued, true, true, false, true, false)]
    [InlineData(TERMINAL_STAGE_ID, WorkStatus.Done, true, true, false, true, false)]
    public async Task GetBoardAsync_SetsShowAndCanResolveConflicts(
        int stageId, WorkStatus status, bool hasConflicts, bool hasPullRequest, bool merged,
        bool expectedShow, bool expectedCanResolve)
    {
        await SeedStagesAsync();
        var taskId = await SeedConflictTaskAsync(
            stageId: stageId,
            status: status,
            runs: [],
            hasConflicts: hasConflicts,
            hasPullRequest: hasPullRequest,
            merged: merged);

        var card = await GetBoardCardAsync(taskId);

        Assert.Equal(expectedShow, card.ShowConflicts);
        Assert.Equal(expectedCanResolve, card.CanResolveConflicts);
    }

    [Theory]
    // La más reciente es el arreglo de conflictos activo.
    [InlineData(RunStatus.Failed, false, RunStatus.Queued, true, true)]
    [InlineData(RunStatus.Succeeded, false, RunStatus.Running, true, true)]
    // La más reciente es un arreglo que ya terminó.
    [InlineData(RunStatus.Succeeded, false, RunStatus.Failed, true, false)]
    // Una ejecución anterior de arreglo, aunque siga activa, no cuenta si la más reciente no es un arreglo.
    [InlineData(RunStatus.Queued, true, RunStatus.Running, false, false)]
    public async Task GetDetailsViewAsync_ResolvingConflicts_UsesMostRecentRun(
        RunStatus olderStatus, bool olderResolve, RunStatus newerStatus, bool newerResolve, bool expected)
    {
        await SeedStagesAsync();
        var taskId = await SeedConflictTaskAsync(
            stageId: DEVELOPER_STAGE_ID,
            status: WorkStatus.Running,
            runs: [(olderStatus, olderResolve), (newerStatus, newerResolve)]);

        await using var db = CreateDbContext();
        var details = await CreateRepository(db).GetDetailsViewAsync(id: taskId, runId: null, ct: CancellationToken.None);

        Assert.NotNull(details);
        Assert.Equal(expected, details.ResolvingConflicts);
    }

    [Fact]
    public async Task GetDetailsViewAsync_SetsShowAndCanResolveConflictsFromTaskAndStage()
    {
        await SeedStagesAsync();
        var approvalId = await SeedConflictTaskAsync(
            stageId: APPROVAL_STAGE_ID,
            status: WorkStatus.WaitingApproval,
            runs: [(RunStatus.Succeeded, resolveConflicts: false)]);
        var terminalId = await SeedConflictTaskAsync(
            stageId: TERMINAL_STAGE_ID,
            status: WorkStatus.Done,
            runs: []);

        await using var db = CreateDbContext();
        var repository = CreateRepository(db);
        var approval = await repository.GetDetailsViewAsync(id: approvalId, runId: null, ct: CancellationToken.None);
        var terminal = await repository.GetDetailsViewAsync(id: terminalId, runId: null, ct: CancellationToken.None);

        Assert.NotNull(approval);
        Assert.True(approval.ShowConflicts);
        Assert.True(approval.CanResolveConflicts);
        Assert.False(approval.ResolvingConflicts);
        Assert.NotNull(terminal);
        Assert.True(terminal.ShowConflicts);
        Assert.False(terminal.CanResolveConflicts);
    }

    private async Task<BoardCardViewModel> GetBoardCardAsync(int taskId)
    {
        await using var db = CreateDbContext();
        var board = await CreateRepository(db).GetBoardAsync(new BoardFilterViewModel(), CancellationToken.None);
        return board.Columns.SelectMany(c => c.Tasks).Single(c => c.Item.Id == taskId);
    }

    /// <summary>Proyecto y columnas «Aprobación Producto» (aprobación humana), «Desarrollador» (agente con push) y «Terminado».</summary>
    private async Task SeedStagesAsync()
    {
        await using var db = CreateDbContext();
        db.Projects.Add(new Project { Id = 1, Name = "Proyecto", RepoOwner = "o", RepoName = "r" });
        db.Stages.AddRange(
            new Stage { Id = APPROVAL_STAGE_ID, Name = "Aprobación Producto", Order = 3, Kind = StageKind.HumanApproval },
            new Stage { Id = DEVELOPER_STAGE_ID, Name = "Desarrollador", Order = 1, Kind = StageKind.Agent, PushChanges = true },
            new Stage { Id = TERMINAL_STAGE_ID, Name = "Terminado", Order = 9, Kind = StageKind.Terminal });
        await db.SaveChangesAsync();
    }

    /// <summary>Crea una tarea con pull request y sus ejecuciones, de la más antigua a la más reciente; devuelve su id.</summary>
    private async Task<int> SeedConflictTaskAsync(
        int stageId, WorkStatus status, (RunStatus status, bool resolveConflicts)[] runs,
        bool hasConflicts = true, bool hasPullRequest = true, bool merged = false)
    {
        await using var db = CreateDbContext();
        var task = NewTask("Tarea");
        task.StageId = stageId;
        task.Status = status;
        task.PullRequestHasConflicts = hasConflicts;
        task.PullRequestNumber = hasPullRequest ? 42 : null;
        task.PullRequestMergedAt = merged ? DateTimeOffset.UtcNow : null;
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        foreach (var run in runs)
        {
            db.TaskRuns.Add(new TaskRun { TaskId = task.Id, StageId = stageId, Status = run.status, ResolveConflicts = run.resolveConflicts });
            await db.SaveChangesAsync(); // Uno por uno para que los ids sigan el orden de creación.
        }
        return task.Id;
    }

    private AppDbContext CreateDbContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(_DatabaseName)
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options);

    private static TasksRepository CreateRepository(AppDbContext db) => new TestableTasksRepository(db);

    private static TaskItem NewTask(string title) => new()
    {
        Title = title,
        ProjectId = 1,
        StageId = 1,
        CreatedByName = "tester",
    };

    /// <summary>Crea proyecto, columna y una tarea con un adjunto (contenido en la base de datos).</summary>
    private async Task SeedTaskAsync(RunStatus? runStatus)
    {
        await using var db = CreateDbContext();
        db.Projects.Add(new Project { Id = 1, Name = "Proyecto", RepoOwner = "o", RepoName = "r" });
        db.Stages.Add(new Stage { Id = 1, Name = "Backlog", Kind = StageKind.Manual });
        var task = NewTask("Tarea");
        task.Attachments.Add(new TaskAttachment
        {
            FileName = "a.png",
            Kind = AttachmentKind.Image,
            Content = new TaskAttachmentContent { Data = [1, 2, 3] },
        });
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        if (runStatus is not null)
        {
            db.TaskRuns.Add(new TaskRun { TaskId = task.Id, StageId = 1, Status = runStatus.Value });
            await db.SaveChangesAsync();
        }
    }

    /// <summary>El proveedor en memoria no ejecuta SQL, así que se omiten los bloqueos FOR UPDATE de PostgreSQL.</summary>
    private sealed class TestableTasksRepository(AppDbContext db)
        : TasksRepository(db, NullLogger<TasksRepository>.Instance)
    {
        protected override Task LockTaskAndRunsAsync(int taskId, CancellationToken ct) => Task.CompletedTask;
    }
}
