using DevArv.Contracts.Models;
using DevArv.Tests.TestSupport;
using DevArv.Web.DataModels;
using DevArv.Web.DatabaseConnection;
using DevArv.Web.Enums.Runs;
using DevArv.Web.Helpers;
using DevArv.Web.Repositories;
using DevArv.Web.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace DevArv.Tests.Web.Repositories;

public sealed class WorkersRepositoryTests
{
    private readonly string _DatabaseName = Guid.NewGuid().ToString("N");

    private AppDbContext CreateDbContext() => TestDatabase.CreateContext(_DatabaseName);

    private async Task<WorkerNode> SeedWorkerAsync(string Name, string Key = "dvw_clave", bool IsEnabled = true)
    {
        await using var db = CreateDbContext();
        var worker = new WorkerNode { Name = Name, KeyHash = ApiKeys.Hash(Key), IsEnabled = IsEnabled };
        db.Workers.Add(worker);
        await db.SaveChangesAsync();
        return worker;
    }

    private async Task SeedRunAsync(int WorkerID, RunStatus Status, string StageName = "Desarrollador")
    {
        await using var db = CreateDbContext();
        if (!await db.Projects.AnyAsync())
        {
            db.Projects.Add(new Project { Id = 1, Name = "P", RepoOwner = "o", RepoName = "r" });
            db.Stages.Add(new Stage { Id = 1, Name = "S" });
            await db.SaveChangesAsync();
        }
        var task = new TaskItem { Title = "T", ProjectId = 1, StageId = 1 };
        db.Tasks.Add(task);
        await db.SaveChangesAsync();
        db.TaskRuns.Add(new TaskRun { TaskId = task.Id, StageId = 1, StageName = StageName, Status = Status, WorkerId = WorkerID });
        await db.SaveChangesAsync();
    }

    // ------------------------------------------------------------------ alta y claves

    [Fact]
    public async Task InsertAsync_CreatesTheWorkerAndReturnsAKeyThatMatchesTheStoredHash()
    {
        await using var db = CreateDbContext();

        var result = await new WorkersRepository(db).InsertAsync(
            Record: new WorkerCreateViewModel { Name = "  PC-Oficina  " },
            CT: CancellationToken.None);

        Assert.Equal("PC-Oficina", result.WorkerName);
        Assert.StartsWith("dvw_", result.Key);
        await using var check = CreateDbContext();
        var worker = await check.Workers.SingleAsync();
        Assert.Equal(ApiKeys.Hash(result.Key), worker.KeyHash);
        Assert.Equal(result.Key[..10], worker.KeyPrefix);
        Assert.True(worker.IsEnabled);
    }

    [Fact]
    public async Task InsertAsync_DuplicateName_Throws()
    {
        await SeedWorkerAsync("PC-1");
        await using var db = CreateDbContext();

        var ex = await Assert.ThrowsAsync<ValidationFailedException>(() => new WorkersRepository(db).InsertAsync(
            Record: new WorkerCreateViewModel { Name = "PC-1" },
            CT: CancellationToken.None));

        Assert.Equal("Input.Name", ex.Errors.Single().Field);
        await using var check = CreateDbContext();
        Assert.Equal(1, await check.Workers.CountAsync());
    }

    [Fact]
    public async Task RegenerateKeyAsync_ReplacesTheKeyAndInvalidatesTheOldOne()
    {
        var worker = await SeedWorkerAsync("PC-1", "dvw_vieja");
        await using var db = CreateDbContext();

        var result = await new WorkersRepository(db).RegenerateKeyAsync(
            ID: worker.Id,
            CT: CancellationToken.None);

        Assert.NotNull(result);
        await using var check = CreateDbContext();
        var stored = await check.Workers.SingleAsync();
        Assert.Equal(ApiKeys.Hash(result.Key), stored.KeyHash);
        Assert.NotEqual(ApiKeys.Hash("dvw_vieja"), stored.KeyHash);
    }

    [Fact]
    public async Task RegenerateKeyAsync_UnknownWorker_ReturnsNull()
    {
        await using var db = CreateDbContext();

        var result = await new WorkersRepository(db).RegenerateKeyAsync(
            ID: 999,
            CT: CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ToggleEnabledAsync_FlipsTheFlag()
    {
        var worker = await SeedWorkerAsync("PC-1");
        await using var db = CreateDbContext();
        var repository = new WorkersRepository(db);

        Assert.True(await repository.ToggleEnabledAsync(
            ID: worker.Id,
            CT: CancellationToken.None));
        await using (var check = CreateDbContext())
            Assert.False((await check.Workers.SingleAsync()).IsEnabled);

        await repository.ToggleEnabledAsync(
            ID: worker.Id,
            CT: CancellationToken.None);
        await using var checkAgain = CreateDbContext();
        Assert.True((await checkAgain.Workers.SingleAsync()).IsEnabled);
    }

    [Fact]
    public async Task ToggleEnabledAsync_UnknownWorker_ReturnsFalse()
    {
        await using var db = CreateDbContext();

        var found = await new WorkersRepository(db).ToggleEnabledAsync(
            ID: 999,
            CT: CancellationToken.None);

        Assert.False(found);
    }

    // ------------------------------------------------------------------ borrar

    [Fact]
    public async Task DeleteAsync_WithRunningRun_Throws()
    {
        var worker = await SeedWorkerAsync("PC-1");
        await SeedRunAsync(worker.Id, RunStatus.Running);
        await using var db = CreateDbContext();

        var ex = await Assert.ThrowsAsync<ValidationFailedException>(() => new WorkersRepository(db).DeleteAsync(
            ID: worker.Id,
            CT: CancellationToken.None));

        Assert.Contains("ejecución en curso", ex.Message);
        await using var check = CreateDbContext();
        Assert.Equal(1, await check.Workers.CountAsync());
    }

    [Fact]
    public async Task DeleteAsync_WithFinishedRuns_RemovesTheWorker()
    {
        var worker = await SeedWorkerAsync("PC-1");
        await SeedRunAsync(worker.Id, RunStatus.Succeeded);
        await using var db = CreateDbContext();

        await new WorkersRepository(db).DeleteAsync(
            ID: worker.Id,
            CT: CancellationToken.None);

        await using var check = CreateDbContext();
        Assert.Equal(0, await check.Workers.CountAsync());
    }

    [Fact]
    public async Task DeleteAsync_UnknownWorker_DoesNotThrow()
    {
        await using var db = CreateDbContext();

        await new WorkersRepository(db).DeleteAsync(
            ID: 999,
            CT: CancellationToken.None);
    }

    // ------------------------------------------------------------------ lecturas

    [Fact]
    public async Task GetEnabledAsync_ReturnsOnlyEnabledWorkersOrderedByName()
    {
        await SeedWorkerAsync("B", "k1");
        await SeedWorkerAsync("A", "k2");
        await SeedWorkerAsync("C", "k3", IsEnabled: false);
        await using var db = CreateDbContext();

        var result = await new WorkersRepository(db).GetEnabledAsync(CancellationToken.None);

        Assert.Equal(["A", "B"], result.Select(w => w.Name));
    }

    [Fact]
    public async Task GetCurrentRunsAsync_ListsOnlyRunningRunsPerWorker()
    {
        var busy = await SeedWorkerAsync("PC-1", "k1");
        var idle = await SeedWorkerAsync("PC-2", "k2");
        await SeedRunAsync(busy.Id, RunStatus.Running, "Desarrollador");
        await SeedRunAsync(busy.Id, RunStatus.Succeeded, "Revisor");
        await using var db = CreateDbContext();

        var result = await new WorkersRepository(db).GetCurrentRunsAsync(CancellationToken.None);

        Assert.Single(result);
        Assert.Contains("Desarrollador", result[busy.Id]);
        Assert.DoesNotContain("Revisor", result[busy.Id]);
        Assert.False(result.ContainsKey(idle.Id));
    }

    // ------------------------------------------------------------------ API del worker

    [Fact]
    public async Task AuthenticateAsync_ValidKey_ReturnsTheWorkerAndRecordsTheConnection()
    {
        await SeedWorkerAsync("PC-1", "dvw_correcta");
        await using var db = CreateDbContext();

        var worker = await new WorkersRepository(db).AuthenticateAsync(
            KeyHash: ApiKeys.Hash("dvw_correcta"),
            CT: CancellationToken.None);

        Assert.NotNull(worker);
        await using var check = CreateDbContext();
        Assert.NotNull((await check.Workers.SingleAsync()).LastSeenAt);
    }

    [Fact]
    public async Task AuthenticateAsync_UnknownKey_ReturnsNull()
    {
        await SeedWorkerAsync("PC-1", "dvw_correcta");
        await using var db = CreateDbContext();

        var worker = await new WorkersRepository(db).AuthenticateAsync(
            KeyHash: ApiKeys.Hash("dvw_otra"),
            CT: CancellationToken.None);

        Assert.Null(worker);
    }

    [Fact]
    public async Task AuthenticateAsync_DisabledWorker_ReturnsNullAndDoesNotRecordAConnection()
    {
        await SeedWorkerAsync("PC-1", "dvw_correcta", IsEnabled: false);
        await using var db = CreateDbContext();

        var worker = await new WorkersRepository(db).AuthenticateAsync(
            KeyHash: ApiKeys.Hash("dvw_correcta"),
            CT: CancellationToken.None);

        Assert.Null(worker);
        await using var check = CreateDbContext();
        Assert.Null((await check.Workers.SingleAsync()).LastSeenAt);
    }

    [Fact]
    public async Task RecordHeartbeatAsync_StoresTheReportedData()
    {
        await SeedWorkerAsync("PC-1");
        await using var db = CreateDbContext();
        var worker = await db.Workers.SingleAsync();

        await new WorkersRepository(db).RecordHeartbeatAsync(
            Worker: worker,
            Request: new HeartbeatRequest("MAC-1", "1.2.3", "macOS", 2),
            CT: CancellationToken.None);

        await using var check = CreateDbContext();
        var stored = await check.Workers.SingleAsync();
        Assert.Equal("MAC-1", stored.MachineName);
        Assert.Equal("1.2.3", stored.Version);
        Assert.Equal("macOS", stored.OsDescription);
        Assert.Equal(2, stored.RunningJobs);
    }

    [Fact]
    public async Task RecordHeartbeatAsync_TruncatesLongTextsAndClampsNegativeCounts()
    {
        await SeedWorkerAsync("PC-1");
        await using var db = CreateDbContext();
        var worker = await db.Workers.SingleAsync();

        await new WorkersRepository(db).RecordHeartbeatAsync(
            Worker: worker,
            Request: new HeartbeatRequest(new string('m', 500), new string('v', 500), null, -3),
            CT: CancellationToken.None);

        await using var check = CreateDbContext();
        var stored = await check.Workers.SingleAsync();
        Assert.Equal(100, stored.MachineName!.Length);
        Assert.Equal(40, stored.Version!.Length);
        Assert.Null(stored.OsDescription);
        Assert.Equal(0, stored.RunningJobs);
    }

    [Theory]
    [InlineData(null, 5, null)]
    [InlineData("abc", 5, "abc")]
    [InlineData("abcde", 5, "abcde")]
    [InlineData("abcdef", 5, "abcde")]
    public void Limit_CutsTheValueToTheMaximum(string? Value, int Max, string? Expected) =>
        Assert.Equal(Expected, WorkersRepository.Limit(Value, Max));
}
