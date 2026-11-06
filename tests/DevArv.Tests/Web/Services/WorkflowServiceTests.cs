using DevArv.Web.DatabaseConnection;
using DevArv.Web.DataModels;
using DevArv.Web.Enums.Runs;
using DevArv.Web.Enums.Tasks;
using DevArv.Web.Enums.Workflow;
using DevArv.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DevArv.Tests.Web.Services;

/// <summary>Conflictos del pull request: detección (orquestador) y botón «Arreglar conflictos».</summary>
public sealed class WorkflowServiceTests
{
    private const int DeveloperId = 4;
    private const int ProductApprovalId = 7;
    private const int PendingDeployId = 9;

    private readonly string _DatabaseName = Guid.NewGuid().ToString("N");

    [Fact]
    public async Task ResolveConflictsAsync_ReturnsTaskToDeveloperWithConflictRun()
    {
        var taskId = await SeedTaskAsync(hasConflicts: true);

        await using (var db = CreateDbContext())
        {
            var workflow = CreateService(db);
            var task = (await workflow.LoadTaskAsync(taskId, CancellationToken.None))!;
            await workflow.ResolveConflictsAsync(task, "ana", CancellationToken.None);
        }

        await using var check = CreateDbContext();
        var saved = await check.Tasks.SingleAsync();
        Assert.Equal(DeveloperId, saved.StageId);
        Assert.Equal(WorkStatus.Queued, saved.Status);
        Assert.True(saved.PullRequestHasConflicts); // Lo limpia el orquestador cuando GitHub deja de informarlos.
        var run = await check.TaskRuns.SingleAsync();
        Assert.True(run.ResolveConflicts);
        Assert.Equal(RunStatus.Queued, run.Status);
        Assert.Equal(StageKind.Agent, run.RunnerKind);
        Assert.Contains(await check.TaskEvents.Select(e => e.Message).ToListAsync(),
            m => m.Contains("arreglar los conflictos del pull request #12"));
    }

    [Fact]
    public async Task ResolveConflictsAsync_WithoutConflicts_Throws()
    {
        var taskId = await SeedTaskAsync(hasConflicts: false);

        await using var db = CreateDbContext();
        var workflow = CreateService(db);
        var task = (await workflow.LoadTaskAsync(taskId, CancellationToken.None))!;

        var ex = await Assert.ThrowsAsync<WorkflowException>(() => workflow.ResolveConflictsAsync(task, "ana", CancellationToken.None));
        Assert.Contains("no informa conflictos", ex.Message);
        Assert.Equal(0, await db.TaskRuns.CountAsync());
    }

    [Theory]
    [InlineData(WorkStatus.Queued)]
    [InlineData(WorkStatus.Running)]
    public async Task ResolveConflictsAsync_WhileRunning_Throws(WorkStatus status)
    {
        var taskId = await SeedTaskAsync(hasConflicts: true, status: status);

        await using var db = CreateDbContext();
        var workflow = CreateService(db);
        var task = (await workflow.LoadTaskAsync(taskId, CancellationToken.None))!;

        var ex = await Assert.ThrowsAsync<WorkflowException>(() => workflow.ResolveConflictsAsync(task, "ana", CancellationToken.None));
        Assert.Contains("ya se está ejecutando", ex.Message);
    }

    [Fact]
    public async Task RetryAsync_AfterFailedConflictRun_KeepsResolvingConflicts()
    {
        var taskId = await SeedTaskAsync(hasConflicts: true);
        await using (var db = CreateDbContext())
        {
            var workflow = CreateService(db);
            await workflow.ResolveConflictsAsync((await workflow.LoadTaskAsync(taskId, CancellationToken.None))!, "ana", CancellationToken.None);
            var run = await db.TaskRuns.SingleAsync();
            run.Status = RunStatus.Failed;
            var task = await db.Tasks.SingleAsync();
            task.Status = WorkStatus.Failed;
            await db.SaveChangesAsync();
        }

        await using (var db = CreateDbContext())
        {
            var workflow = CreateService(db);
            await workflow.RetryAsync((await workflow.LoadTaskAsync(taskId, CancellationToken.None))!, "ana", CancellationToken.None);
        }

        await using var check = CreateDbContext();
        var latest = await check.TaskRuns.OrderByDescending(r => r.Id).FirstAsync();
        Assert.Equal(RunStatus.Queued, latest.Status);
        Assert.True(latest.ResolveConflicts);
    }

    [Fact]
    public async Task OnPullRequestConflictsCheckedAsync_RecordsChangesOnlyOnce()
    {
        var taskId = await SeedTaskAsync(hasConflicts: false);

        await using (var db = CreateDbContext())
        {
            var workflow = CreateService(db);
            await workflow.OnPullRequestConflictsCheckedAsync(taskId, 12, true, "Sistema", CancellationToken.None);
            await workflow.OnPullRequestConflictsCheckedAsync(taskId, 12, true, "Sistema", CancellationToken.None);
        }

        await using (var check = CreateDbContext())
        {
            Assert.True((await check.Tasks.SingleAsync()).PullRequestHasConflicts);
            Assert.Single(await check.TaskEvents.Where(e => e.Message.Contains("tiene conflictos")).ToListAsync());
        }

        await using (var db = CreateDbContext())
        {
            await CreateService(db).OnPullRequestConflictsCheckedAsync(taskId, 12, false, "Sistema", CancellationToken.None);
        }

        await using var final = CreateDbContext();
        Assert.False((await final.Tasks.SingleAsync()).PullRequestHasConflicts);
        Assert.Single(await final.TaskEvents.Where(e => e.Message.Contains("ya no tiene conflictos")).ToListAsync());
    }

    [Fact]
    public async Task OnPullRequestConflictsCheckedAsync_OtherPullRequest_IsIgnored()
    {
        var taskId = await SeedTaskAsync(hasConflicts: false);

        await using (var db = CreateDbContext())
        {
            await CreateService(db).OnPullRequestConflictsCheckedAsync(taskId, 99, true, "Sistema", CancellationToken.None);
        }

        await using var check = CreateDbContext();
        Assert.False((await check.Tasks.SingleAsync()).PullRequestHasConflicts);
        Assert.Equal(0, await check.TaskEvents.CountAsync());
    }

    [Fact]
    public async Task OnPullRequestMergedAsync_ClearsConflicts()
    {
        var taskId = await SeedTaskAsync(hasConflicts: true);

        await using (var db = CreateDbContext())
        {
            await CreateService(db).OnPullRequestMergedAsync(taskId, 12, "Sistema", CancellationToken.None);
        }

        await using var check = CreateDbContext();
        var task = await check.Tasks.SingleAsync();
        Assert.False(task.PullRequestHasConflicts);
        Assert.Equal(PendingDeployId, task.StageId);
    }

    [Fact]
    public async Task GetPullRequestsToWatchAsync_IncludesLastKnownConflicts()
    {
        var taskId = await SeedTaskAsync(hasConflicts: true);

        await using var db = CreateDbContext();
        var watched = Assert.Single(await CreateService(db).GetPullRequestsToWatchAsync(CancellationToken.None));
        Assert.Equal(taskId, watched.TaskId);
        Assert.Equal(12, watched.Number);
        Assert.True(watched.HasConflicts);
    }

    [Fact]
    public async Task GetPullRequestsToWatchAsync_WithoutMergeDestination_StillWatchesForConflicts()
    {
        await SeedTaskAsync(hasConflicts: false);
        await using (var db = CreateDbContext())
        {
            (await db.Stages.SingleAsync(s => s.Id == PendingDeployId)).IsMergeDestination = false;
            await db.SaveChangesAsync();
        }

        await using var check = CreateDbContext();
        Assert.Single(await CreateService(check).GetPullRequestsToWatchAsync(CancellationToken.None));
    }

    private AppDbContext CreateDbContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(_DatabaseName)
        .Options);

    // Estas operaciones no usan los adjuntos.
    private static WorkflowService CreateService(AppDbContext db) =>
        new(db, null!, NullLogger<WorkflowService>.Instance);

    /// <summary>Flujo mínimo (Desarrollador → Aprobación Producto → Pendiente Deploy) y una tarea con PR #12 en Aprobación Producto.</summary>
    private async Task<int> SeedTaskAsync(bool hasConflicts, WorkStatus status = WorkStatus.WaitingApproval)
    {
        await using var db = CreateDbContext();
        db.Projects.Add(new Project { Id = 1, Name = "Proyecto", RepoOwner = "o", RepoName = "r", IsActive = true });
        db.Stages.AddRange(
            new Stage { Id = DeveloperId, Name = "Desarrollador", Order = 40, Kind = StageKind.Agent, PushChanges = true },
            new Stage { Id = ProductApprovalId, Name = "Aprobación Producto", Order = 70, Kind = StageKind.HumanApproval,
                RejectStageId = DeveloperId },
            new Stage { Id = PendingDeployId, Name = "Pendiente Deploy", Order = 90, Kind = StageKind.Manual, IsMergeDestination = true });
        var task = new TaskItem
        {
            Title = "Tarea", ProjectId = 1, StageId = ProductApprovalId, Status = status, CreatedByName = "ana",
            BaseBranch = "main", WorkBranch = "fd_/1-tarea", PullRequestNumber = 12,
            PullRequestUrl = "https://github.com/o/r/pull/12", PullRequestHasConflicts = hasConflicts,
        };
        db.Tasks.Add(task);
        await db.SaveChangesAsync();
        return task.Id;
    }
}
