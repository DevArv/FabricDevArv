using DevArv.Contracts.Models;
using DevArv.Tests.TestSupport;
using DevArv.Web.DataModels;
using DevArv.Web.DatabaseConnection;
using DevArv.Web.Enums.Runs;
using DevArv.Web.Enums.Tasks;
using DevArv.Web.Enums.Workflow;
using DevArv.Web.Models;
using DevArv.Web.Repositories;
using DevArv.Web.Services;
using DevArv.Web.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DevArv.Tests.Web.Services;

public sealed class WorkflowServiceTests : IDisposable
{
    // Columnas del flujo de las pruebas.
    private const int BACKLOG = 1;
    private const int APPROVAL = 2;
    private const int DEVELOPER = 3;
    private const int REVIEWER = 4;
    private const int PENDING_DEPLOY = 5;
    private const int DONE = 6;

    private readonly string _DatabaseName = Guid.NewGuid().ToString("N");
    private readonly string _StorageDirectory = Path.Combine(Path.GetTempPath(), $"devarv-workflow-{Guid.NewGuid():N}");

    public WorkflowServiceTests() => Directory.CreateDirectory(_StorageDirectory);

    public void Dispose()
    {
        if (Directory.Exists(_StorageDirectory)) Directory.Delete(_StorageDirectory, recursive: true);
    }

    private AppDbContext CreateDbContext() => TestDatabase.CreateContext(_DatabaseName);

    private AttachmentStorage CreateStorage() =>
        new(Options.Create(new AttachmentOptions { StoragePath = _StorageDirectory }), null!);

    private WorkflowService CreateService(AppDbContext Db) =>
        new(Db, CreateStorage(), NullLogger<WorkflowService>.Instance);

    private async Task SeedFlowAsync()
    {
        await using var db = CreateDbContext();
        db.Projects.Add(new Project { ID = 1, Name = "Fabric", RepoOwner = "acme", RepoName = "fabric", BranchPrefix = "devarv/" });
        db.Projects.Add(new Project { ID = 2, Name = "Inactivo", RepoOwner = "acme", RepoName = "old", IsActive = false });
        db.Workers.Add(new WorkerNode { ID = 1, Name = "PC-1", KeyHash = "h1" });
        db.Workers.Add(new WorkerNode { ID = 2, Name = "PC-2", KeyHash = "h2", IsEnabled = false });
        db.Stages.AddRange(
            new Stage { ID = BACKLOG, Name = "Backlog", Order = 10, Kind = StageKind.MANUAL, IsInitial = true, SuccessStageID = APPROVAL },
            new Stage { ID = APPROVAL, Name = "Aprobación", Order = 20, Kind = StageKind.HUMAN_APPROVAL, SuccessStageID = DEVELOPER, RejectStageID = BACKLOG },
            new Stage { ID = DEVELOPER, Name = "Desarrollador", Order = 30, Kind = StageKind.AGENT, PushChanges = true, SuccessStageID = REVIEWER, TimeoutMinutes = 30 },
            new Stage { ID = REVIEWER, Name = "Revisor", Order = 40, Kind = StageKind.AGENT, RequiresDecision = true, SuccessStageID = PENDING_DEPLOY, RejectStageID = DEVELOPER, TimeoutMinutes = 30 },
            new Stage { ID = PENDING_DEPLOY, Name = "Pendiente Deploy", Order = 50, Kind = StageKind.MANUAL, IsMergeDestination = true, SuccessStageID = DONE },
            new Stage { ID = DONE, Name = "Terminado", Order = 60, Kind = StageKind.TERMINAL });
        await db.SaveChangesAsync();
    }

    private static TaskCreateViewModel NewRecord(bool StartNow = false, int? PreferredWorkerID = null, int ProjectID = 1) => new()
    {
        Title = "  Mi tarea  ",
        Description = "Detalle",
        ProjectID = ProjectID,
        BaseBranch = "main",
        StartNow = StartNow,
        PreferredWorkerID = PreferredWorkerID,
    };

    private async Task<int> CreateTaskAsync(bool StartNow = false)
    {
        await using var db = CreateDbContext();
        var task = await CreateService(db).CreateTaskAsync(
            Record: NewRecord(StartNow),
            Files: [],
            Actor: "tester",
            UserID: "user-1",
            CT: CancellationToken.None);
        return task.ID;
    }

    /// <summary>Crea una tarea y la mueve a la columna; si es de agente deja su ejecución «en curso» en la PC 1.</summary>
    private async Task<(int TaskID, long RunID)> TaskRunningInAsync(int StageID)
    {
        var taskID = await CreateTaskAsync();
        await using (var db = CreateDbContext())
        {
            var service = CreateService(db);
            var task = await service.LoadTaskAsync(taskID, CancellationToken.None);
            await service.MoveAsync(
                Item: task!,
                StageID: StageID,
                Actor: "tester",
                CT: CancellationToken.None);
        }

        await using var mark = CreateDbContext();
        var run = await mark.TaskRuns.SingleAsync(r => r.TaskID == taskID && r.Status == RunStatus.QUEUED);
        run.Status = RunStatus.RUNNING;
        run.WorkerID = 1;
        run.StartedAt = DateTimeOffset.UtcNow;
        run.LastActivityAt = DateTimeOffset.UtcNow;
        var taskRow = await mark.Tasks.SingleAsync(t => t.ID == taskID);
        taskRow.Status = WorkStatus.RUNNING;
        await mark.SaveChangesAsync();
        return (taskID, run.ID);
    }

    private static RunCompletion Completion(
        bool Success = true, string? ResultText = null, IReadOnlyList<ChangedFile>? ChangedFiles = null,
        bool Cancelled = false, string? ErrorMessage = null) =>
        new(Success, Cancelled, ErrorMessage, ResultText, 0.5m, 10, 20, 30, 40, 3, "sesion", "opus", "abc123", true,
            ChangedFiles ?? []);

    private static readonly IReadOnlyList<ChangedFile> _OneFile = [new ChangedFile("M", "src/A.cs")];

    private async Task CompleteAsync(long RunID, RunCompletion Completion)
    {
        await using var db = CreateDbContext();
        await CreateService(db).CompleteRunAsync(
            RunID: RunID,
            Completion: Completion,
            Actor: "PC-1",
            CT: CancellationToken.None);
    }

    // ------------------------------------------------------------------ crear tarea

    [Fact]
    public async Task CreateTaskAsync_ValidRecord_CreatesTheTaskInTheInitialStage()
    {
        await SeedFlowAsync();

        var taskID = await CreateTaskAsync();

        await using var check = CreateDbContext();
        var task = await check.Tasks.SingleAsync(t => t.ID == taskID);
        Assert.Equal("Mi tarea", task.Title);
        Assert.Equal(BACKLOG, task.StageID);
        Assert.Equal(WorkStatus.IDLE, task.Status);
        Assert.Equal("tester", task.CreatedByName);
        Assert.Equal("user-1", task.CreatedByID);
        Assert.Contains("creó la tarea en «Backlog»", (await check.TaskEvents.SingleAsync()).Message);
    }

    [Fact]
    public async Task CreateTaskAsync_StartNow_QueuesTheFirstDeveloperStage()
    {
        await SeedFlowAsync();

        var taskID = await CreateTaskAsync(StartNow: true);

        await using var check = CreateDbContext();
        var task = await check.Tasks.SingleAsync(t => t.ID == taskID);
        Assert.Equal(DEVELOPER, task.StageID);
        Assert.Equal(WorkStatus.QUEUED, task.Status);
        Assert.Equal($"devarv/{taskID}-mi-tarea", task.WorkBranch);
        var run = await check.TaskRuns.SingleAsync();
        Assert.Equal(RunStatus.QUEUED, run.Status);
        Assert.Equal(StageKind.AGENT, run.RunnerKind);
    }

    [Fact]
    public async Task CreateTaskAsync_PreferredWorker_IsKeptOnlyWhenTheWorkerIsEnabled()
    {
        await SeedFlowAsync();
        await using var db = CreateDbContext();
        var service = CreateService(db);

        var withEnabled = await service.CreateTaskAsync(
            Record: NewRecord(PreferredWorkerID: 1),
            Files: [],
            Actor: "tester",
            UserID: null,
            CT: CancellationToken.None);
        var withDisabled = await service.CreateTaskAsync(
            Record: NewRecord(PreferredWorkerID: 2),
            Files: [],
            Actor: "tester",
            UserID: null,
            CT: CancellationToken.None);

        Assert.Equal(1, withEnabled.PreferredWorkerID);
        Assert.Null(withDisabled.PreferredWorkerID);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(999)]
    public async Task CreateTaskAsync_InactiveOrUnknownProject_ThrowsAndCreatesNothing(int ProjectID)
    {
        await SeedFlowAsync();
        await using var db = CreateDbContext();

        var ex = await Assert.ThrowsAsync<ValidationFailedException>(() => CreateService(db).CreateTaskAsync(
            Record: NewRecord(ProjectID: ProjectID),
            Files: [],
            Actor: "tester",
            UserID: null,
            CT: CancellationToken.None));

        Assert.Contains(ex.Errors, e => e.Field == "Input.ProjectId");
        await using var check = CreateDbContext();
        Assert.Equal(0, await check.Tasks.CountAsync());
    }

    [Fact]
    public async Task CreateTaskAsync_InvalidFile_ThrowsAndCreatesNothing()
    {
        await SeedFlowAsync();
        await using var db = CreateDbContext();

        var ex = await Assert.ThrowsAsync<ValidationFailedException>(() => CreateService(db).CreateTaskAsync(
            Record: NewRecord(),
            Files: [TestFiles.Create("malo.exe", [1, 2, 3])],
            Actor: "tester",
            UserID: null,
            CT: CancellationToken.None));

        Assert.Contains(ex.Errors, e => e.Field == "Files");
        await using var check = CreateDbContext();
        Assert.Equal(0, await check.Tasks.CountAsync());
        Assert.Empty(Directory.GetFiles(_StorageDirectory));
    }

    [Fact]
    public async Task CreateTaskAsync_WithFiles_StoresThemAndLinksThemToTheTask()
    {
        await SeedFlowAsync();
        await using var db = CreateDbContext();

        var task = await CreateService(db).CreateTaskAsync(
            Record: NewRecord(),
            Files: [TestFiles.CreatePng("uno.png")],
            Actor: "tester",
            UserID: null,
            CT: CancellationToken.None);

        await using var check = CreateDbContext();
        var attachment = await check.TaskAttachments.SingleAsync();
        Assert.Equal(task.ID, attachment.TaskID);
        Assert.Single(Directory.GetFiles(_StorageDirectory));
    }

    [Fact]
    public async Task CreateTaskAsync_WhenTheFinalSaveFails_DeletesTheStoredFiles()
    {
        await SeedFlowAsync();
        // El primer guardado inserta la tarea; el segundo (adjuntos + entrada a la columna) falla.
        await using var db = new FailingDbContext(TestDatabase.CreateOptions(_DatabaseName), FailOnSave: 2);

        await Assert.ThrowsAsync<DbUpdateException>(() => CreateService(db).CreateTaskAsync(
            Record: NewRecord(),
            Files: [TestFiles.CreatePng()],
            Actor: "tester",
            UserID: null,
            CT: CancellationToken.None));

        Assert.Empty(Directory.GetFiles(_StorageDirectory));
    }

    [Fact]
    public async Task GetInitialStageAsync_WithoutStages_ThrowsAWorkflowException()
    {
        await using var db = CreateDbContext();

        await Assert.ThrowsAsync<WorkflowException>(() => CreateService(db).GetInitialStageAsync(CancellationToken.None));
    }

    // ------------------------------------------------------------------ acciones de usuario

    [Fact]
    public async Task ApproveAsync_HumanApprovalStage_MovesToTheSuccessStage()
    {
        await SeedFlowAsync();
        var (taskID, _) = await TaskRunningInAsync(DEVELOPER);
        await using (var arrange = CreateDbContext())
        {
            var service = CreateService(arrange);
            var task = await service.LoadTaskAsync(taskID, CancellationToken.None);
            await service.MoveAsync(
                Item: task!,
                StageID: APPROVAL,
                Actor: "tester",
                CT: CancellationToken.None);
        }

        await using var db = CreateDbContext();
        var approving = CreateService(db);
        var loaded = await approving.LoadTaskAsync(taskID, CancellationToken.None);
        await approving.ApproveAsync(
            Item: loaded!,
            Actor: "ana",
            CT: CancellationToken.None);

        await using var check = CreateDbContext();
        var result = await check.Tasks.SingleAsync(t => t.ID == taskID);
        Assert.Equal(DEVELOPER, result.StageID);
        Assert.Equal(WorkStatus.QUEUED, result.Status);
        Assert.Contains(await check.TaskEvents.ToListAsync(), e => e.Message.Contains("ana aprobó en «Aprobación»"));
    }

    [Fact]
    public async Task ApproveAsync_ManualStage_SaysItAdvanced()
    {
        await SeedFlowAsync();
        var taskID = await CreateTaskAsync();
        await using var db = CreateDbContext();
        var service = CreateService(db);
        var task = await service.LoadTaskAsync(taskID, CancellationToken.None);

        await service.ApproveAsync(
            Item: task!,
            Actor: "ana",
            CT: CancellationToken.None);

        await using var check = CreateDbContext();
        Assert.Equal(APPROVAL, (await check.Tasks.SingleAsync()).StageID);
        Assert.Contains(await check.TaskEvents.ToListAsync(), e => e.Message.Contains("ana avanzó en «Backlog»"));
    }

    [Theory]
    [InlineData(DEVELOPER)]
    [InlineData(DONE)]
    public async Task ApproveAsync_StageThatIsNotApprovedByAPerson_Throws(int StageID)
    {
        await SeedFlowAsync();
        var taskID = await CreateTaskAsync();
        await using var db = CreateDbContext();
        var service = CreateService(db);
        var task = await service.LoadTaskAsync(taskID, CancellationToken.None);
        await service.MoveAsync(
            Item: task!,
            StageID: StageID,
            Actor: "tester",
            CT: CancellationToken.None);

        await Assert.ThrowsAsync<WorkflowException>(() => service.ApproveAsync(
            Item: task!,
            Actor: "ana",
            CT: CancellationToken.None));
    }

    [Fact]
    public async Task RequestChangesAsync_EmptyFeedback_Throws()
    {
        await SeedFlowAsync();
        var taskID = await CreateTaskAsync();
        await using var db = CreateDbContext();
        var service = CreateService(db);
        var task = await service.LoadTaskAsync(taskID, CancellationToken.None);

        await Assert.ThrowsAsync<WorkflowException>(() => service.RequestChangesAsync(
            Item: task!,
            Actor: "ana",
            Feedback: "   ",
            CT: CancellationToken.None));
    }

    [Fact]
    public async Task RequestChangesAsync_StageWithRejectDestination_SavesTheFeedbackAndMovesThere()
    {
        await SeedFlowAsync();
        var taskID = await CreateTaskAsync();
        await using var db = CreateDbContext();
        var service = CreateService(db);
        var task = await service.LoadTaskAsync(taskID, CancellationToken.None);
        await service.MoveAsync(
            Item: task!,
            StageID: APPROVAL,
            Actor: "tester",
            CT: CancellationToken.None);

        await service.RequestChangesAsync(
            Item: task!,
            Actor: "ana",
            Feedback: "  Falta el botón  ",
            CT: CancellationToken.None);

        await using var check = CreateDbContext();
        Assert.Equal(BACKLOG, (await check.Tasks.SingleAsync()).StageID);
        var comment = await check.TaskComments.SingleAsync();
        Assert.Equal(CommentKind.FEEDBACK, comment.Kind);
        Assert.Equal("Falta el botón", comment.Body);
    }

    [Fact]
    public async Task RequestChangesAsync_StageWithoutRejectDestination_GoesToTheFirstDeveloperStage()
    {
        await SeedFlowAsync();
        var taskID = await CreateTaskAsync();
        await using var db = CreateDbContext();
        var service = CreateService(db);
        var task = await service.LoadTaskAsync(taskID, CancellationToken.None);

        await service.RequestChangesAsync(
            Item: task!,
            Actor: "ana",
            Feedback: "Ajusta esto",
            CT: CancellationToken.None);

        await using var check = CreateDbContext();
        Assert.Equal(DEVELOPER, (await check.Tasks.SingleAsync()).StageID);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("  ", true)]
    [InlineData("Nota", false)]
    public async Task AddCommentAsync_RejectsEmptyBodies(string Body, bool ShouldThrow)
    {
        await SeedFlowAsync();
        var taskID = await CreateTaskAsync();
        await using var db = CreateDbContext();
        var service = CreateService(db);
        var task = await service.LoadTaskAsync(taskID, CancellationToken.None);

        var act = () => service.AddCommentAsync(
            Item: task!,
            Actor: "ana",
            Body: Body,
            AsFeedback: true,
            CT: CancellationToken.None);

        if (ShouldThrow)
        {
            await Assert.ThrowsAsync<WorkflowException>(act);
            return;
        }
        await act();
        await using var check = CreateDbContext();
        Assert.Equal(CommentKind.FEEDBACK, (await check.TaskComments.SingleAsync()).Kind);
    }

    [Fact]
    public async Task RetryAsync_ManualStage_Throws()
    {
        await SeedFlowAsync();
        var taskID = await CreateTaskAsync();
        await using var db = CreateDbContext();
        var service = CreateService(db);
        var task = await service.LoadTaskAsync(taskID, CancellationToken.None);

        await Assert.ThrowsAsync<WorkflowException>(() => service.RetryAsync(
            Item: task!,
            Actor: "ana",
            CT: CancellationToken.None));
    }

    [Fact]
    public async Task RetryAsync_AgentStage_CancelsTheQueuedRunAndQueuesANewOne()
    {
        await SeedFlowAsync();
        var taskID = await CreateTaskAsync(StartNow: true);
        await using var db = CreateDbContext();
        var service = CreateService(db);
        var task = await service.LoadTaskAsync(taskID, CancellationToken.None);

        await service.RetryAsync(
            Item: task!,
            Actor: "ana",
            CT: CancellationToken.None);

        await using var check = CreateDbContext();
        var runs = await check.TaskRuns.OrderBy(r => r.ID).ToListAsync();
        Assert.Equal([RunStatus.CANCELLED, RunStatus.QUEUED], runs.Select(r => r.Status));
    }

    [Fact]
    public async Task CancelAsync_WithoutActiveRuns_Throws()
    {
        await SeedFlowAsync();
        var taskID = await CreateTaskAsync();
        await using var db = CreateDbContext();
        var service = CreateService(db);
        var task = await service.LoadTaskAsync(taskID, CancellationToken.None);

        await Assert.ThrowsAsync<WorkflowException>(() => service.CancelAsync(
            Item: task!,
            Actor: "ana",
            CT: CancellationToken.None));
    }

    [Fact]
    public async Task CancelAsync_QueuedRun_CancelsItAndLeavesTheTaskIdle()
    {
        await SeedFlowAsync();
        var taskID = await CreateTaskAsync(StartNow: true);
        await using var db = CreateDbContext();
        var service = CreateService(db);
        var task = await service.LoadTaskAsync(taskID, CancellationToken.None);

        await service.CancelAsync(
            Item: task!,
            Actor: "ana",
            CT: CancellationToken.None);

        await using var check = CreateDbContext();
        var result = await check.Tasks.SingleAsync();
        Assert.Equal(WorkStatus.IDLE, result.Status);
        Assert.Equal("Cancelado por ana", result.LastError);
        Assert.Equal(RunStatus.CANCELLED, (await check.TaskRuns.SingleAsync()).Status);
    }

    [Fact]
    public async Task SetArchivedAsync_ArchivingCancelsQueuedRunsAndRestoringClearsTheDate()
    {
        await SeedFlowAsync();
        var taskID = await CreateTaskAsync(StartNow: true);
        await using var db = CreateDbContext();
        var service = CreateService(db);
        var task = await service.LoadTaskAsync(taskID, CancellationToken.None);

        await service.SetArchivedAsync(
            Item: task!,
            Archived: true,
            Actor: "ana",
            CT: CancellationToken.None);
        await using (var archived = CreateDbContext())
        {
            Assert.NotNull((await archived.Tasks.SingleAsync()).ArchivedAt);
            Assert.Equal(RunStatus.CANCELLED, (await archived.TaskRuns.SingleAsync()).Status);
        }

        await service.SetArchivedAsync(
            Item: task!,
            Archived: false,
            Actor: "ana",
            CT: CancellationToken.None);
        await using var restored = CreateDbContext();
        Assert.Null((await restored.Tasks.SingleAsync()).ArchivedAt);
    }

    [Fact]
    public async Task MoveAsync_UnknownStage_Throws()
    {
        await SeedFlowAsync();
        var taskID = await CreateTaskAsync();
        await using var db = CreateDbContext();
        var service = CreateService(db);
        var task = await service.LoadTaskAsync(taskID, CancellationToken.None);

        await Assert.ThrowsAsync<WorkflowException>(() => service.MoveAsync(
            Item: task!,
            StageID: 999,
            Actor: "ana",
            CT: CancellationToken.None));
    }

    [Fact]
    public async Task MoveAsync_TerminalStage_MarksTheTaskAsDone()
    {
        await SeedFlowAsync();
        var taskID = await CreateTaskAsync();
        await using var db = CreateDbContext();
        var service = CreateService(db);
        var task = await service.LoadTaskAsync(taskID, CancellationToken.None);

        await service.MoveAsync(
            Item: task!,
            StageID: DONE,
            Actor: "ana",
            CT: CancellationToken.None);

        await using var check = CreateDbContext();
        Assert.Equal(WorkStatus.DONE, (await check.Tasks.SingleAsync()).Status);
    }

    [Fact]
    public async Task DropOnStageAsync_UnknownTask_ReturnsFalse()
    {
        await SeedFlowAsync();
        await using var db = CreateDbContext();

        var found = await CreateService(db).DropOnStageAsync(
            TaskID: 999,
            StageID: DEVELOPER,
            Actor: "ana",
            CT: CancellationToken.None);

        Assert.False(found);
    }

    [Fact]
    public async Task DropOnStageAsync_SameStage_DoesNothing_AndAnotherStageMovesTheTask()
    {
        await SeedFlowAsync();
        var taskID = await CreateTaskAsync();
        await using var db = CreateDbContext();
        var service = CreateService(db);

        Assert.True(await service.DropOnStageAsync(
            TaskID: taskID,
            StageID: BACKLOG,
            Actor: "ana",
            CT: CancellationToken.None));
        await using (var same = CreateDbContext())
            Assert.Equal(1, await same.TaskEvents.CountAsync());

        Assert.True(await service.DropOnStageAsync(
            TaskID: taskID,
            StageID: APPROVAL,
            Actor: "ana",
            CT: CancellationToken.None));
        await using var moved = CreateDbContext();
        Assert.Equal(APPROVAL, (await moved.Tasks.SingleAsync()).StageID);
    }

    // ------------------------------------------------------------------ resultado de una ejecución

    [Fact]
    public async Task CompleteRunAsync_DeveloperWithChanges_RecordsTheRunAndSendsTheTaskToTheReviewer()
    {
        await SeedFlowAsync();
        var (taskID, runID) = await TaskRunningInAsync(DEVELOPER);

        await CompleteAsync(runID, Completion(ChangedFiles: _OneFile, ResultText: "Listo"));

        await using var check = CreateDbContext();
        var run = await check.TaskRuns.SingleAsync(r => r.ID == runID);
        Assert.Equal(RunStatus.SUCCEEDED, run.Status);
        Assert.Equal("Listo", run.Summary);
        Assert.Equal("abc123", run.HeadCommit);
        Assert.Contains("src/A.cs", run.ChangedFilesJson);
        var task = await check.Tasks.SingleAsync(t => t.ID == taskID);
        Assert.Equal(REVIEWER, task.StageID);
        Assert.Equal(WorkStatus.QUEUED, task.Status);
        Assert.Equal(0.5m, task.TotalCostUsd);
        Assert.Equal(100, task.TotalTokens);
        Assert.Equal(1, await check.TaskRuns.CountAsync(r => r.StageID == REVIEWER && r.Status == RunStatus.QUEUED));
    }

    [Fact]
    public async Task CompleteRunAsync_DeveloperWithoutChanges_FailsTheTask()
    {
        await SeedFlowAsync();
        var (taskID, runID) = await TaskRunningInAsync(DEVELOPER);

        await CompleteAsync(runID, Completion(ChangedFiles: []));

        await using var check = CreateDbContext();
        var task = await check.Tasks.SingleAsync(t => t.ID == taskID);
        Assert.Equal(WorkStatus.FAILED, task.Status);
        Assert.Equal(DEVELOPER, task.StageID);
        Assert.Contains("sin cambios", task.LastError);
        Assert.Equal(RunStatus.FAILED, (await check.TaskRuns.SingleAsync()).Status);
    }

    [Fact]
    public async Task CompleteRunAsync_ChangedFilesMissingInThePayload_IsTreatedAsNoChangesInsteadOfCrashing()
    {
        await SeedFlowAsync();
        var (taskID, runID) = await TaskRunningInAsync(DEVELOPER);
        var payload = Completion() with { ChangedFiles = null! };

        await CompleteAsync(runID, payload);

        await using var check = CreateDbContext();
        var task = await check.Tasks.SingleAsync(t => t.ID == taskID);
        Assert.Equal(WorkStatus.FAILED, task.Status);
        Assert.Null((await check.TaskRuns.SingleAsync()).ChangedFilesJson);
    }

    [Fact]
    public async Task CompleteRunAsync_ReviewerApproves_MovesToTheSuccessStage()
    {
        await SeedFlowAsync();
        var (taskID, runID) = await TaskRunningInAsync(REVIEWER);

        await CompleteAsync(runID, Completion(ResultText: "Todo bien.\nVEREDICTO: APROBADO"));

        await using var check = CreateDbContext();
        var task = await check.Tasks.SingleAsync(t => t.ID == taskID);
        Assert.Equal(PENDING_DEPLOY, task.StageID);
        Assert.Equal(RunDecision.APPROVED, (await check.TaskRuns.SingleAsync()).Decision);
    }

    [Fact]
    public async Task CompleteRunAsync_ReviewerRejects_SavesTheNoteAndReturnsToTheDeveloper()
    {
        await SeedFlowAsync();
        var (taskID, runID) = await TaskRunningInAsync(REVIEWER);

        await CompleteAsync(runID, Completion(ResultText: "Falta una prueba.\nVEREDICTO: RECHAZADO"));

        await using var check = CreateDbContext();
        var task = await check.Tasks.SingleAsync(t => t.ID == taskID);
        Assert.Equal(DEVELOPER, task.StageID);
        Assert.Equal(WorkStatus.QUEUED, task.Status);
        var note = await check.TaskComments.SingleAsync();
        Assert.Equal(CommentKind.REVIEWER_NOTE, note.Kind);
        Assert.Contains("Falta una prueba", note.Body);
    }

    [Fact]
    public async Task CompleteRunAsync_ReviewerWithoutVerdict_FailsTheTaskAndKeepsItInTheStage()
    {
        await SeedFlowAsync();
        var (taskID, runID) = await TaskRunningInAsync(REVIEWER);

        await CompleteAsync(runID, Completion(ResultText: "Se ve bien"));

        await using var check = CreateDbContext();
        var task = await check.Tasks.SingleAsync(t => t.ID == taskID);
        Assert.Equal(WorkStatus.FAILED, task.Status);
        Assert.Equal(REVIEWER, task.StageID);
        Assert.Contains("veredicto", task.LastError);
    }

    [Fact]
    public async Task CompleteRunAsync_FailedRun_FailsTheRunAndTheTaskWithTheError()
    {
        await SeedFlowAsync();
        var (taskID, runID) = await TaskRunningInAsync(DEVELOPER);

        await CompleteAsync(runID, Completion(Success: false, ErrorMessage: "git falló\0"));

        await using var check = CreateDbContext();
        var run = await check.TaskRuns.SingleAsync();
        Assert.Equal(RunStatus.FAILED, run.Status);
        Assert.Equal("git falló", run.ErrorMessage);
        Assert.Equal(WorkStatus.FAILED, (await check.Tasks.SingleAsync(t => t.ID == taskID)).Status);
    }

    [Fact]
    public async Task CompleteRunAsync_FailedRunWithoutMessage_UsesAGenericOne()
    {
        await SeedFlowAsync();
        var (_, runID) = await TaskRunningInAsync(DEVELOPER);

        await CompleteAsync(runID, Completion(Success: false));

        await using var check = CreateDbContext();
        Assert.Equal("La ejecución falló sin detalle.", (await check.TaskRuns.SingleAsync()).ErrorMessage);
    }

    [Fact]
    public async Task CompleteRunAsync_CancelledByTheWorker_LeavesTheTaskIdle()
    {
        await SeedFlowAsync();
        var (taskID, runID) = await TaskRunningInAsync(DEVELOPER);

        await CompleteAsync(runID, Completion(Success: false, Cancelled: true));

        await using var check = CreateDbContext();
        Assert.Equal(RunStatus.CANCELLED, (await check.TaskRuns.SingleAsync()).Status);
        var task = await check.Tasks.SingleAsync(t => t.ID == taskID);
        Assert.Equal(WorkStatus.IDLE, task.Status);
        Assert.Equal(DEVELOPER, task.StageID);
    }

    [Fact]
    public async Task CompleteRunAsync_RunThatIsNotRunning_IsIgnored()
    {
        await SeedFlowAsync();
        var (taskID, runID) = await TaskRunningInAsync(DEVELOPER);
        await CompleteAsync(runID, Completion(ChangedFiles: _OneFile));

        await CompleteAsync(runID, Completion(Success: false, ErrorMessage: "tarde"));

        await using var check = CreateDbContext();
        Assert.Equal(RunStatus.SUCCEEDED, (await check.TaskRuns.SingleAsync(r => r.ID == runID)).Status);
        Assert.Equal(REVIEWER, (await check.Tasks.SingleAsync(t => t.ID == taskID)).StageID);
    }

    [Fact]
    public async Task CompleteRunAsync_UnknownRun_IsIgnored()
    {
        await SeedFlowAsync();

        await CompleteAsync(999, Completion());
    }

    // ------------------------------------------------------------------ ejecuciones colgadas

    private async Task<long> SeedStalledRunAsync(DateTimeOffset LastActivity, DateTimeOffset? WorkerSeen, DateTimeOffset? StartedAt = null)
    {
        var (_, runID) = await TaskRunningInAsync(DEVELOPER);
        await using var db = CreateDbContext();
        var run = await db.TaskRuns.SingleAsync(r => r.ID == runID);
        run.LastActivityAt = LastActivity;
        run.StartedAt = StartedAt ?? LastActivity;
        var worker = await db.Workers.SingleAsync(w => w.ID == 1);
        worker.LastSeenAt = WorkerSeen;
        await db.SaveChangesAsync();
        return runID;
    }

    [Fact]
    public async Task FailStalledRunsAsync_SilentRunWhoseWorkerStoppedReporting_IsFailed()
    {
        await SeedFlowAsync();
        var old = DateTimeOffset.UtcNow.AddMinutes(-10);
        var runID = await SeedStalledRunAsync(LastActivity: old, WorkerSeen: old);
        await using var db = CreateDbContext();

        var failed = await CreateService(db).FailStalledRunsAsync(
            WorkerSilence: TimeSpan.FromMinutes(3),
            CT: CancellationToken.None);

        Assert.Equal(1, failed);
        await using var check = CreateDbContext();
        var run = await check.TaskRuns.SingleAsync(r => r.ID == runID);
        Assert.Equal(RunStatus.FAILED, run.Status);
        Assert.Contains("dejó de responder", run.ErrorMessage);
        Assert.Equal(WorkStatus.FAILED, (await check.Tasks.SingleAsync()).Status);
    }

    [Fact]
    public async Task FailStalledRunsAsync_SilentRunButWorkerStillSendsHeartbeats_IsKept()
    {
        await SeedFlowAsync();
        await SeedStalledRunAsync(LastActivity: DateTimeOffset.UtcNow.AddMinutes(-10), WorkerSeen: DateTimeOffset.UtcNow);
        await using var db = CreateDbContext();

        var failed = await CreateService(db).FailStalledRunsAsync(
            WorkerSilence: TimeSpan.FromMinutes(3),
            CT: CancellationToken.None);

        Assert.Equal(0, failed);
    }

    [Fact]
    public async Task FailStalledRunsAsync_RunThatExceededTheStageTimeout_IsFailed()
    {
        await SeedFlowAsync();
        // Columna con 30 min de tiempo máximo + 15 de margen; lleva 60 min pero sigue activa.
        var runID = await SeedStalledRunAsync(
            LastActivity: DateTimeOffset.UtcNow,
            WorkerSeen: DateTimeOffset.UtcNow,
            StartedAt: DateTimeOffset.UtcNow.AddMinutes(-60));
        await using var db = CreateDbContext();

        var failed = await CreateService(db).FailStalledRunsAsync(
            WorkerSilence: TimeSpan.FromMinutes(3),
            CT: CancellationToken.None);

        Assert.Equal(1, failed);
        await using var check = CreateDbContext();
        Assert.Contains("tiempo máximo", (await check.TaskRuns.SingleAsync(r => r.ID == runID)).ErrorMessage);
    }

    [Fact]
    public async Task FailStalledRunsAsync_HealthyRun_IsKept()
    {
        await SeedFlowAsync();
        await TaskRunningInAsync(DEVELOPER);
        await using var db = CreateDbContext();

        var failed = await CreateService(db).FailStalledRunsAsync(
            WorkerSilence: TimeSpan.FromMinutes(3),
            CT: CancellationToken.None);

        Assert.Equal(0, failed);
    }

    // ------------------------------------------------------------------ merge del pull request

    private async Task SetPullRequestAsync(int TaskID, int Number)
    {
        await using var db = CreateDbContext();
        var task = await db.Tasks.SingleAsync(t => t.ID == TaskID);
        task.PullRequestNumber = Number;
        await db.SaveChangesAsync();
    }

    private async Task MoveTaskAsync(int TaskID, int StageID)
    {
        await using var db = CreateDbContext();
        var service = CreateService(db);
        var task = await service.LoadTaskAsync(TaskID, CancellationToken.None);
        await service.MoveAsync(
            Item: task!,
            StageID: StageID,
            Actor: "tester",
            CT: CancellationToken.None);
    }

    [Fact]
    public async Task GetPullRequestsToWatchAsync_OnlyTasksBeforeTheMergeDestinationWithAnOpenPullRequest()
    {
        await SeedFlowAsync();
        var before = await CreateTaskAsync();
        var after = await CreateTaskAsync();
        var withoutPullRequest = await CreateTaskAsync();
        var archived = await CreateTaskAsync();
        await MoveTaskAsync(after, DONE);
        await SetPullRequestAsync(before, 11);
        await SetPullRequestAsync(after, 12);
        await SetPullRequestAsync(archived, 13);
        await using (var db = CreateDbContext())
        {
            (await db.Tasks.SingleAsync(t => t.ID == archived)).ArchivedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        await using var check = CreateDbContext();
        var watched = await CreateService(check).GetPullRequestsToWatchAsync(CancellationToken.None);

        var item = Assert.Single(watched);
        Assert.Equal(before, item.TaskID);
        Assert.Equal(11, item.Number);
        Assert.Equal("acme", item.RepoOwner);
        Assert.DoesNotContain(watched, w => w.TaskID == withoutPullRequest);
    }

    [Fact]
    public async Task GetPullRequestsToWatchAsync_WithoutMergeDestination_ReturnsNothing()
    {
        await SeedFlowAsync();
        var taskID = await CreateTaskAsync();
        await SetPullRequestAsync(taskID, 11);
        await using (var db = CreateDbContext())
        {
            (await db.Stages.SingleAsync(s => s.ID == PENDING_DEPLOY)).IsMergeDestination = false;
            await db.SaveChangesAsync();
        }

        await using var check = CreateDbContext();
        var watched = await CreateService(check).GetPullRequestsToWatchAsync(CancellationToken.None);

        Assert.Empty(watched);
    }

    [Fact]
    public async Task OnPullRequestMergedAsync_TaskBeforeTheDestination_MovesItAndRecordsTheMerge()
    {
        await SeedFlowAsync();
        var taskID = await CreateTaskAsync();
        await SetPullRequestAsync(taskID, 11);
        await using var db = CreateDbContext();

        await CreateService(db).OnPullRequestMergedAsync(
            TaskID: taskID,
            Number: 11,
            Actor: "Sistema",
            CT: CancellationToken.None);

        await using var check = CreateDbContext();
        var task = await check.Tasks.SingleAsync();
        Assert.Equal(PENDING_DEPLOY, task.StageID);
        Assert.NotNull(task.PullRequestMergedAt);
    }

    [Fact]
    public async Task OnPullRequestMergedAsync_TaskAlreadyPastTheDestination_OnlyRecordsTheMerge()
    {
        await SeedFlowAsync();
        var taskID = await CreateTaskAsync();
        await SetPullRequestAsync(taskID, 11);
        await MoveTaskAsync(taskID, DONE);
        await using var db = CreateDbContext();

        await CreateService(db).OnPullRequestMergedAsync(
            TaskID: taskID,
            Number: 11,
            Actor: "Sistema",
            CT: CancellationToken.None);

        await using var check = CreateDbContext();
        var task = await check.Tasks.SingleAsync();
        Assert.Equal(DONE, task.StageID);
        Assert.NotNull(task.PullRequestMergedAt);
    }

    [Fact]
    public async Task OnPullRequestMergedAsync_DifferentPullRequestNumber_IsIgnored()
    {
        await SeedFlowAsync();
        var taskID = await CreateTaskAsync();
        await SetPullRequestAsync(taskID, 11);
        await using var db = CreateDbContext();

        await CreateService(db).OnPullRequestMergedAsync(
            TaskID: taskID,
            Number: 99,
            Actor: "Sistema",
            CT: CancellationToken.None);

        await using var check = CreateDbContext();
        var task = await check.Tasks.SingleAsync();
        Assert.Equal(BACKLOG, task.StageID);
        Assert.Null(task.PullRequestMergedAt);
    }

    [Fact]
    public async Task OnPullRequestMergedAsync_UnknownTask_DoesNothing()
    {
        await SeedFlowAsync();
        await using var db = CreateDbContext();

        await CreateService(db).OnPullRequestMergedAsync(
            TaskID: 999,
            Number: 1,
            Actor: "Sistema",
            CT: CancellationToken.None);
    }

    // ------------------------------------------------------------------ utilidades

    [Fact]
    public void Clean_RemovesNullCharacters_AndKeepsNull()
    {
        Assert.Equal("ab", WorkflowService.Clean("a\0b"));
        Assert.Null(WorkflowService.Clean(null));
    }

    [Fact]
    public async Task AddEvent_TruncatesMessagesLongerThanTheColumn()
    {
        await SeedFlowAsync();
        var taskID = await CreateTaskAsync();
        await using var db = CreateDbContext();
        var service = CreateService(db);

        service.AddEvent(
            TaskID: taskID,
            Actor: "ana",
            Message: new string('x', 5000));
        await db.SaveChangesAsync();

        await using var check = CreateDbContext();
        var longest = await check.TaskEvents.OrderByDescending(e => e.Message.Length).FirstAsync();
        Assert.Equal(1000, longest.Message.Length);
        Assert.EndsWith("…", longest.Message);
    }
}
