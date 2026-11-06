using DevArv.Tests.TestSupport;
using DevArv.Web.DataModels;
using DevArv.Web.DatabaseConnection;
using DevArv.Web.Enums.Runs;
using DevArv.Web.Repositories;

namespace DevArv.Tests.Web.Repositories;

public sealed class RunsRepositoryTests
{
    private readonly string _DatabaseName = Guid.NewGuid().ToString("N");

    private AppDbContext CreateDbContext() => TestDatabase.CreateContext(_DatabaseName);

    private async Task<long> SeedRunAsync(int WorkerID, RunStatus Status, bool CancelRequested = false)
    {
        await using var db = CreateDbContext();
        db.Projects.Add(new Project { Id = 1, Name = "P", RepoOwner = "o", RepoName = "r" });
        db.Stages.Add(new Stage { Id = 1, Name = "S" });
        await db.SaveChangesAsync();
        var task = new TaskItem { Title = "T", ProjectId = 1, StageId = 1 };
        db.Tasks.Add(task);
        await db.SaveChangesAsync();
        var run = new TaskRun { TaskId = task.Id, StageId = 1, Status = Status, WorkerId = WorkerID, CancelRequested = CancelRequested };
        db.TaskRuns.Add(run);
        await db.SaveChangesAsync();
        return run.Id;
    }

    [Fact]
    public async Task IsAssignedToWorkerAsync_OnlyForTheWorkerThatTookTheRun()
    {
        var runID = await SeedRunAsync(WorkerID: 7, RunStatus.Running);
        await using var db = CreateDbContext();
        var repository = new RunsRepository(db);

        Assert.True(await repository.IsAssignedToWorkerAsync(
            RunID: runID,
            WorkerID: 7,
            CT: CancellationToken.None));
        Assert.False(await repository.IsAssignedToWorkerAsync(
            RunID: runID,
            WorkerID: 8,
            CT: CancellationToken.None));
        Assert.False(await repository.IsAssignedToWorkerAsync(
            RunID: 999,
            WorkerID: 7,
            CT: CancellationToken.None));
    }

    [Fact]
    public async Task AppendLogAsync_RunOfAnotherWorker_ReturnsNullAndWritesNothing()
    {
        var runID = await SeedRunAsync(WorkerID: 7, RunStatus.Running);
        await using var db = CreateDbContext();
        var repository = new RecordingRunsRepository(db);

        var response = await repository.AppendLogAsync(
            RunID: runID,
            WorkerID: 8,
            Text: "hola",
            CT: CancellationToken.None);

        Assert.Null(response);
        Assert.Empty(repository.Writes);
    }

    [Theory]
    [InlineData(RunStatus.Queued)]
    [InlineData(RunStatus.Succeeded)]
    [InlineData(RunStatus.Failed)]
    [InlineData(RunStatus.Cancelled)]
    public async Task AppendLogAsync_RunNoLongerRunning_TellsTheWorkerToStop(RunStatus Status)
    {
        var runID = await SeedRunAsync(WorkerID: 7, Status);
        await using var db = CreateDbContext();
        var repository = new RecordingRunsRepository(db);

        var response = await repository.AppendLogAsync(
            RunID: runID,
            WorkerID: 7,
            Text: "hola",
            CT: CancellationToken.None);

        Assert.NotNull(response);
        Assert.True(response.CancelRequested);
        Assert.Empty(repository.Writes);
    }

    [Fact]
    public async Task AppendLogAsync_RunningRun_WritesTheCleanedText()
    {
        var runID = await SeedRunAsync(WorkerID: 7, RunStatus.Running);
        await using var db = CreateDbContext();
        var repository = new RecordingRunsRepository(db);

        var response = await repository.AppendLogAsync(
            RunID: runID,
            WorkerID: 7,
            Text: "lí\0nea",
            CT: CancellationToken.None);

        Assert.NotNull(response);
        Assert.False(response.CancelRequested);
        var write = Assert.Single(repository.Writes);
        Assert.Equal(runID, write.RunID);
        Assert.Equal("línea", write.Text);
    }

    [Fact]
    public async Task AppendLogAsync_NullText_OnlyMarksActivity()
    {
        var runID = await SeedRunAsync(WorkerID: 7, RunStatus.Running);
        await using var db = CreateDbContext();
        var repository = new RecordingRunsRepository(db);

        await repository.AppendLogAsync(
            RunID: runID,
            WorkerID: 7,
            Text: null,
            CT: CancellationToken.None);

        Assert.Equal("", Assert.Single(repository.Writes).Text);
    }

    [Fact]
    public async Task AppendLogAsync_RunWithPendingCancellation_ReportsIt()
    {
        var runID = await SeedRunAsync(WorkerID: 7, RunStatus.Running, CancelRequested: true);
        await using var db = CreateDbContext();
        var repository = new RecordingRunsRepository(db);

        var response = await repository.AppendLogAsync(
            RunID: runID,
            WorkerID: 7,
            Text: "x",
            CT: CancellationToken.None);

        Assert.True(response!.CancelRequested);
    }

    /// <summary>El proveedor en memoria no admite ExecuteUpdate: se registra lo que se habría escrito.</summary>
    private sealed class RecordingRunsRepository(AppDbContext Db) : RunsRepository(Db)
    {
        public List<(long RunID, string Text)> Writes { get; } = [];

        protected override Task WriteLogAsync(long RunID, string Text, DateTimeOffset Now, CancellationToken CT)
        {
            Writes.Add((RunID, Text));
            return Task.CompletedTask;
        }
    }
}
