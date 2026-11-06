using DevArv.Tests.TestSupport;
using DevArv.Web.Constants;
using DevArv.Web.DataModels;
using DevArv.Web.DatabaseConnection;
using DevArv.Web.Enums.Runs;
using DevArv.Web.Enums.Workflow;
using DevArv.Web.Repositories;
using DevArv.Web.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace DevArv.Tests.Web.Repositories;

public sealed class StagesRepositoryTests
{
    private readonly string _DatabaseName = Guid.NewGuid().ToString("N");

    private AppDbContext CreateDbContext() => TestDatabase.CreateContext(_DatabaseName);

    private static StageEditViewModel ValidRecord(string Name = "Nueva") => new()
    {
        Name = Name,
        Kind = StageKind.Manual,
        PermissionMode = "acceptEdits",
    };

    private async Task<Stage> SeedStageAsync(string Name, int Order, Action<Stage>? Configure = null)
    {
        await using var db = CreateDbContext();
        var stage = new Stage { Name = Name, Order = Order, Kind = StageKind.Manual };
        Configure?.Invoke(stage);
        db.Stages.Add(stage);
        await db.SaveChangesAsync();
        return stage;
    }

    // ------------------------------------------------------------------ lecturas

    [Fact]
    public async Task GetAllAsync_ReturnsStagesOrderedByOrder()
    {
        await SeedStageAsync("B", 20);
        await SeedStageAsync("A", 10);

        await using var db = CreateDbContext();
        var result = await new StagesRepository(db).GetAllAsync(CancellationToken.None);

        Assert.Equal(["A", "B"], result.Select(s => s.Name));
    }

    [Fact]
    public async Task GetOthersAsync_ExcludesTheGivenStage()
    {
        var first = await SeedStageAsync("A", 10);
        await SeedStageAsync("B", 20);

        await using var db = CreateDbContext();
        var result = await new StagesRepository(db).GetOthersAsync(
            ExcludedID: first.Id,
            CT: CancellationToken.None);

        Assert.Equal(["B"], result.Select(s => s.Name));
    }

    [Fact]
    public async Task GetOthersAsync_WithoutExcludedID_ReturnsAllStages()
    {
        await SeedStageAsync("A", 10);
        await SeedStageAsync("B", 20);

        await using var db = CreateDbContext();
        var result = await new StagesRepository(db).GetOthersAsync(
            ExcludedID: null,
            CT: CancellationToken.None);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetTaskCountsAsync_CountsTasksPerStage()
    {
        var first = await SeedStageAsync("A", 10);
        await SeedStageAsync("B", 20);
        await using (var db = CreateDbContext())
        {
            db.Projects.Add(new Project { Id = 1, Name = "P", RepoOwner = "o", RepoName = "r" });
            db.Tasks.AddRange(
                new TaskItem { Title = "1", ProjectId = 1, StageId = first.Id },
                new TaskItem { Title = "2", ProjectId = 1, StageId = first.Id });
            await db.SaveChangesAsync();
        }

        await using var check = CreateDbContext();
        var counts = await new StagesRepository(check).GetTaskCountsAsync(CancellationToken.None);

        Assert.Equal(2, counts[first.Id]);
        Assert.Single(counts);
    }

    [Fact]
    public async Task GetEditAsync_ExistingStage_MapsItsFields()
    {
        var stage = await SeedStageAsync("Dev", 10, s =>
        {
            s.Kind = StageKind.Agent;
            s.Skill = "revision";
            s.PushChanges = true;
            s.TimeoutMinutes = 90;
        });

        await using var db = CreateDbContext();
        var record = await new StagesRepository(db).GetEditAsync(
            ID: stage.Id,
            CT: CancellationToken.None);

        Assert.NotNull(record);
        Assert.Equal("Dev", record.Name);
        Assert.Equal(StageKind.Agent, record.Kind);
        Assert.Equal("revision", record.Skill);
        Assert.True(record.PushChanges);
        Assert.Equal(90, record.TimeoutMinutes);
    }

    [Fact]
    public async Task GetEditAsync_UnknownStage_ReturnsNull()
    {
        await using var db = CreateDbContext();

        var record = await new StagesRepository(db).GetEditAsync(
            ID: 999,
            CT: CancellationToken.None);

        Assert.Null(record);
    }

    [Fact]
    public void NewEdit_UsesTheDefaultDeveloperPrompt()
    {
        using var db = CreateDbContext();

        var record = new StagesRepository(db).NewEdit();

        Assert.Equal(DefaultPrompts.DEVELOPER, record.PromptTemplate);
        Assert.Equal(DefaultPrompts.DEVELOPER_ALLOWED_TOOLS, record.AllowedTools);
    }

    // ------------------------------------------------------------------ validación

    [Fact]
    public async Task GetValidationErrorsAsync_ValidRecord_ReturnsNoErrors()
    {
        await using var db = CreateDbContext();

        var errors = await new StagesRepository(db).GetValidationErrorsAsync(
            ID: null,
            Record: ValidRecord(),
            CT: CancellationToken.None);

        Assert.Empty(errors);
    }

    [Fact]
    public async Task GetValidationErrorsAsync_ReportsEveryBrokenRule()
    {
        await using var db = CreateDbContext();
        var record = ValidRecord();
        record.PermissionMode = "inventado";
        record.SuccessStageId = 99;
        record.RejectStageId = null;
        record.RequiresDecision = true;
        record.Kind = StageKind.System;
        record.SystemAction = SystemAction.None;
        record.ExtraArgs = "--model opus";

        var errors = await new StagesRepository(db).GetValidationErrorsAsync(
            ID: null,
            Record: record,
            CT: CancellationToken.None);

        var fields = errors.Select(e => e.Field).ToList();
        Assert.Contains("Input.PermissionMode", fields);
        Assert.Contains("Input.SuccessStageId", fields);
        Assert.Contains("Input.RejectStageId", fields);
        Assert.Contains("Input.SystemAction", fields);
        Assert.Contains("Input.ExtraArgs", fields);
    }

    [Fact]
    public async Task GetValidationErrorsAsync_StageCannotPointToItself()
    {
        var stage = await SeedStageAsync("A", 10);
        await using var db = CreateDbContext();
        var record = ValidRecord("A");
        record.SuccessStageId = stage.Id;

        var errors = await new StagesRepository(db).GetValidationErrorsAsync(
            ID: stage.Id,
            Record: record,
            CT: CancellationToken.None);

        Assert.Contains(errors, e => e.Field == "Input.SuccessStageId");
    }

    [Fact]
    public async Task GetValidationErrorsAsync_ExtraArgsOnePerLine_AreAccepted()
    {
        await using var db = CreateDbContext();
        var record = ValidRecord();
        record.ExtraArgs = "--model\nopus\n--verbose";

        var errors = await new StagesRepository(db).GetValidationErrorsAsync(
            ID: null,
            Record: record,
            CT: CancellationToken.None);

        Assert.Empty(errors);
    }

    // ------------------------------------------------------------------ guardar

    [Fact]
    public async Task SaveAsync_InvalidRecord_ThrowsAndPersistsNothing()
    {
        await using var db = CreateDbContext();
        var record = ValidRecord();
        record.PermissionMode = "inventado";

        var ex = await Assert.ThrowsAsync<ValidationFailedException>(() => new StagesRepository(db).SaveAsync(
            ID: null,
            Record: record,
            CT: CancellationToken.None));

        Assert.Contains(ex.Errors, e => e.Field == "Input.PermissionMode");
        await using var check = CreateDbContext();
        Assert.Equal(0, await check.Stages.CountAsync());
    }

    [Fact]
    public async Task SaveAsync_NewStage_GoesAfterTheLastOne()
    {
        await SeedStageAsync("A", 40);
        await using var db = CreateDbContext();

        var saved = await new StagesRepository(db).SaveAsync(
            ID: null,
            Record: ValidRecord("  Nueva  "),
            CT: CancellationToken.None);

        Assert.True(saved);
        await using var check = CreateDbContext();
        var created = await check.Stages.SingleAsync(s => s.Name == "Nueva");
        Assert.Equal(50, created.Order);
    }

    [Fact]
    public async Task SaveAsync_FirstStage_StartsAtTen()
    {
        await using var db = CreateDbContext();

        await new StagesRepository(db).SaveAsync(
            ID: null,
            Record: ValidRecord(),
            CT: CancellationToken.None);

        await using var check = CreateDbContext();
        Assert.Equal(10, (await check.Stages.SingleAsync()).Order);
    }

    [Fact]
    public async Task SaveAsync_UnknownID_ReturnsFalse()
    {
        await using var db = CreateDbContext();

        var saved = await new StagesRepository(db).SaveAsync(
            ID: 999,
            Record: ValidRecord(),
            CT: CancellationToken.None);

        Assert.False(saved);
    }

    [Fact]
    public async Task SaveAsync_ExistingStage_UpdatesItWithoutChangingItsOrder()
    {
        var stage = await SeedStageAsync("Vieja", 30);
        await using var db = CreateDbContext();
        var record = ValidRecord("Renombrada");
        record.Icon = "";

        var saved = await new StagesRepository(db).SaveAsync(
            ID: stage.Id,
            Record: record,
            CT: CancellationToken.None);

        Assert.True(saved);
        await using var check = CreateDbContext();
        var updated = await check.Stages.SingleAsync();
        Assert.Equal("Renombrada", updated.Name);
        Assert.Equal(30, updated.Order);
        Assert.Equal("📋", updated.Icon);
    }

    [Fact]
    public async Task SaveAsync_NonAgentKind_ClearsAgentOnlyFlags()
    {
        var reject = await SeedStageAsync("Rechazo", 10);
        await using var db = CreateDbContext();
        var record = ValidRecord();
        record.Kind = StageKind.Manual;
        record.Skill = "revision";
        record.PushChanges = true;
        record.RequiresDecision = true;
        record.RejectStageId = reject.Id;
        record.SystemAction = SystemAction.CreatePullRequest;

        await new StagesRepository(db).SaveAsync(
            ID: null,
            Record: record,
            CT: CancellationToken.None);

        await using var check = CreateDbContext();
        var stage = await check.Stages.SingleAsync(s => s.Name == "Nueva");
        Assert.Null(stage.Skill);
        Assert.False(stage.PushChanges);
        Assert.False(stage.RequiresDecision);
        Assert.Equal(SystemAction.None, stage.SystemAction);
    }

    [Fact]
    public async Task SaveAsync_AgentKind_KeepsFlagsAndNormalizesSkill()
    {
        var reject = await SeedStageAsync("Rechazo", 10);
        await using var db = CreateDbContext();
        var record = ValidRecord("Revisor");
        record.Kind = StageKind.Agent;
        record.Skill = " /revision ";
        record.PushChanges = true;
        record.RequiresDecision = true;
        record.RejectStageId = reject.Id;

        await new StagesRepository(db).SaveAsync(
            ID: null,
            Record: record,
            CT: CancellationToken.None);

        await using var check = CreateDbContext();
        var stage = await check.Stages.SingleAsync(s => s.Name == "Revisor");
        Assert.Equal("revision", stage.Skill);
        Assert.True(stage.PushChanges);
        Assert.True(stage.RequiresDecision);
    }

    [Fact]
    public async Task SaveAsync_SystemKind_KeepsTheAction()
    {
        await using var db = CreateDbContext();
        var record = ValidRecord("PR");
        record.Kind = StageKind.System;
        record.SystemAction = SystemAction.CreatePullRequest;

        await new StagesRepository(db).SaveAsync(
            ID: null,
            Record: record,
            CT: CancellationToken.None);

        await using var check = CreateDbContext();
        Assert.Equal(SystemAction.CreatePullRequest, (await check.Stages.SingleAsync()).SystemAction);
    }

    [Fact]
    public async Task SaveAsync_MarkedAsInitial_UnmarksThePreviousInitialStage()
    {
        await SeedStageAsync("Anterior", 10, s => s.IsInitial = true);
        await using var db = CreateDbContext();
        var record = ValidRecord("Nueva inicial");
        record.IsInitial = true;

        await new StagesRepository(db).SaveAsync(
            ID: null,
            Record: record,
            CT: CancellationToken.None);

        await using var check = CreateDbContext();
        Assert.Equal(["Nueva inicial"], await check.Stages.Where(s => s.IsInitial).Select(s => s.Name).ToListAsync());
    }

    [Fact]
    public async Task SaveAsync_UnmarkingInitial_ClearsTheFlag()
    {
        var stage = await SeedStageAsync("Inicial", 10, s => s.IsInitial = true);
        await using var db = CreateDbContext();
        var record = ValidRecord("Inicial");
        record.IsInitial = false;

        await new StagesRepository(db).SaveAsync(
            ID: stage.Id,
            Record: record,
            CT: CancellationToken.None);

        await using var check = CreateDbContext();
        Assert.False((await check.Stages.SingleAsync()).IsInitial);
    }

    [Fact]
    public async Task SaveAsync_MarkedAsMergeDestination_UnmarksThePreviousOne()
    {
        await SeedStageAsync("Deploy", 10, s => s.IsMergeDestination = true);
        await using var db = CreateDbContext();
        var record = ValidRecord("Otra");
        record.IsMergeDestination = true;

        await new StagesRepository(db).SaveAsync(
            ID: null,
            Record: record,
            CT: CancellationToken.None);

        await using var check = CreateDbContext();
        Assert.Equal(["Otra"], await check.Stages.Where(s => s.IsMergeDestination).Select(s => s.Name).ToListAsync());
    }

    // ------------------------------------------------------------------ mover y eliminar

    [Fact]
    public async Task MoveAsync_Down_SwapsWithTheNextStageAndRenumbers()
    {
        var a = await SeedStageAsync("A", 10);
        await SeedStageAsync("B", 20);
        await SeedStageAsync("C", 30);

        await using var db = CreateDbContext();
        await new StagesRepository(db).MoveAsync(
            ID: a.Id,
            Direction: 1,
            CT: CancellationToken.None);

        await using var check = CreateDbContext();
        var ordered = await check.Stages.OrderBy(s => s.Order).ToListAsync();
        Assert.Equal(["B", "A", "C"], ordered.Select(s => s.Name));
        Assert.Equal([10, 20, 30], ordered.Select(s => s.Order));
    }

    [Fact]
    public async Task MoveAsync_Up_SwapsWithThePreviousStage()
    {
        await SeedStageAsync("A", 10);
        var b = await SeedStageAsync("B", 20);

        await using var db = CreateDbContext();
        await new StagesRepository(db).MoveAsync(
            ID: b.Id,
            Direction: -1,
            CT: CancellationToken.None);

        await using var check = CreateDbContext();
        Assert.Equal(["B", "A"], await check.Stages.OrderBy(s => s.Order).Select(s => s.Name).ToListAsync());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public async Task MoveAsync_PastTheEdges_DoesNothing(int Direction)
    {
        var first = await SeedStageAsync("A", 10);
        var last = await SeedStageAsync("B", 20);

        await using var db = CreateDbContext();
        await new StagesRepository(db).MoveAsync(
            ID: Direction < 0 ? first.Id : last.Id,
            Direction: Direction,
            CT: CancellationToken.None);

        await using var check = CreateDbContext();
        Assert.Equal(["A", "B"], await check.Stages.OrderBy(s => s.Order).Select(s => s.Name).ToListAsync());
    }

    [Fact]
    public async Task MoveAsync_UnknownStage_DoesNothing()
    {
        await SeedStageAsync("A", 10);
        await using var db = CreateDbContext();

        await new StagesRepository(db).MoveAsync(
            ID: 999,
            Direction: 1,
            CT: CancellationToken.None);

        await using var check = CreateDbContext();
        Assert.Equal(10, (await check.Stages.SingleAsync()).Order);
    }

    [Fact]
    public async Task DeleteAsync_StageWithTasks_Throws()
    {
        var stage = await SeedStageAsync("A", 10);
        await using (var db = CreateDbContext())
        {
            db.Projects.Add(new Project { Id = 1, Name = "P", RepoOwner = "o", RepoName = "r" });
            db.Tasks.Add(new TaskItem { Title = "T", ProjectId = 1, StageId = stage.Id });
            await db.SaveChangesAsync();
        }

        await using var check = CreateDbContext();
        var ex = await Assert.ThrowsAsync<ValidationFailedException>(() => new StagesRepository(check).DeleteAsync(
            ID: stage.Id,
            CT: CancellationToken.None));

        Assert.Contains("tiene tareas", ex.Message);
        Assert.Equal(1, await check.Stages.CountAsync());
    }

    [Fact]
    public async Task DeleteAsync_StageWithRunHistory_Throws()
    {
        var stage = await SeedStageAsync("A", 10);
        await using (var db = CreateDbContext())
        {
            db.Projects.Add(new Project { Id = 1, Name = "P", RepoOwner = "o", RepoName = "r" });
            var otherStage = new Stage { Name = "Otra", Order = 20 };
            db.Stages.Add(otherStage);
            await db.SaveChangesAsync();
            var task = new TaskItem { Title = "T", ProjectId = 1, StageId = otherStage.Id };
            db.Tasks.Add(task);
            await db.SaveChangesAsync();
            db.TaskRuns.Add(new TaskRun { TaskId = task.Id, StageId = stage.Id, Status = RunStatus.Succeeded });
            await db.SaveChangesAsync();
        }

        await using var check = CreateDbContext();
        var ex = await Assert.ThrowsAsync<ValidationFailedException>(() => new StagesRepository(check).DeleteAsync(
            ID: stage.Id,
            CT: CancellationToken.None));

        Assert.Contains("historial", ex.Message);
    }

    [Fact]
    public async Task DeleteAsync_EmptyStage_RemovesIt()
    {
        var stage = await SeedStageAsync("A", 10);
        await using var db = CreateDbContext();

        await new StagesRepository(db).DeleteAsync(
            ID: stage.Id,
            CT: CancellationToken.None);

        await using var check = CreateDbContext();
        Assert.Equal(0, await check.Stages.CountAsync());
    }

    [Fact]
    public async Task DeleteAsync_UnknownStage_DoesNotThrow()
    {
        await using var db = CreateDbContext();

        await new StagesRepository(db).DeleteAsync(
            ID: 999,
            CT: CancellationToken.None);
    }
}
