using DevArv.Web.DatabaseConnection;
using DevArv.Web.DataModels;
using DevArv.Web.Enums.Runs;
using DevArv.Web.Enums.Tasks;
using DevArv.Web.Enums.Workflow;
using DevArv.Web.Models;
using DevArv.Web.Repositories;
using DevArv.Web.Services;
using DevArv.Web.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DevArv.Tests.Web.Repositories;

public sealed class TasksRepositoryTests : IDisposable
{
    private readonly string _DatabaseName = Guid.NewGuid().ToString("N");
    private readonly string _StorageDirectory = Path.Combine(Path.GetTempPath(), $"devarv-tests-{Guid.NewGuid():N}");

    public TasksRepositoryTests() => Directory.CreateDirectory(_StorageDirectory);

    public void Dispose()
    {
        // Restaura el permiso de escritura por si una prueba lo quitó.
        if (!OperatingSystem.IsWindows() && Directory.Exists(_StorageDirectory))
            File.SetUnixFileMode(_StorageDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        if (Directory.Exists(_StorageDirectory)) Directory.Delete(_StorageDirectory, recursive: true);
    }

    [Fact]
    public async Task DeleteAsync_WithRunningRun_ThrowsAndKeepsTaskAndFiles()
    {
        var storedName = await SeedTaskAsync(RunStatus.RUNNING);

        await using var db = CreateDbContext();
        var repository = CreateRepository(db);
        var task = await db.Tasks.SingleAsync();

        var ex = await Assert.ThrowsAsync<WorkflowException>(() => repository.DeleteAsync(
            Item: task,
            Actor: "tester",
            CT: CancellationToken.None));

        Assert.Contains("se está ejecutando", ex.Message);
        await using var check = CreateDbContext();
        Assert.Equal(1, await check.Tasks.CountAsync());
        Assert.Equal(1, await check.TaskAttachments.CountAsync());
        Assert.True(File.Exists(Path.Combine(_StorageDirectory, storedName)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(RunStatus.QUEUED)]
    [InlineData(RunStatus.SUCCEEDED)]
    [InlineData(RunStatus.FAILED)]
    [InlineData(RunStatus.CANCELLED)]
    public async Task DeleteAsync_WithoutRunningRun_DeletesTaskAttachmentsAndFiles(RunStatus? RunStatus)
    {
        var storedName = await SeedTaskAsync(RunStatus);

        await using var db = CreateDbContext();
        var repository = CreateRepository(db);
        var task = await db.Tasks.SingleAsync();

        await repository.DeleteAsync(
            Item: task,
            Actor: "tester",
            CT: CancellationToken.None);

        await using var check = CreateDbContext();
        Assert.Equal(0, await check.Tasks.CountAsync());
        Assert.Equal(0, await check.TaskAttachments.CountAsync());
        Assert.False(File.Exists(Path.Combine(_StorageDirectory, storedName)));
    }

    [Fact]
    public async Task DeleteAsync_WithRunningRunOfAnotherTask_DeletesTask()
    {
        await SeedTaskAsync(RunStatus: null);
        await using (var seed = CreateDbContext())
        {
            var other = NewTask(Title: "Otra");
            seed.Tasks.Add(other);
            await seed.SaveChangesAsync();
            seed.TaskRuns.Add(new TaskRun { TaskID = other.ID, StageID = other.StageID, Status = RunStatus.RUNNING });
            await seed.SaveChangesAsync();
        }

        await using var db = CreateDbContext();
        var repository = CreateRepository(db);
        var task = await db.Tasks.SingleAsync(t => t.Title == "Tarea");

        await repository.DeleteAsync(
            Item: task,
            Actor: "tester",
            CT: CancellationToken.None);

        await using var check = CreateDbContext();
        Assert.Equal(["Otra"], await check.Tasks.Select(t => t.Title).ToListAsync());
    }

    [Fact]
    public async Task DeleteAsync_WhenFileCannotBeDeleted_StillDeletesTaskAndTriesRemainingFiles()
    {
        if (OperatingSystem.IsWindows()) return; // El bloqueo se simula con permisos POSIX de la carpeta.

        var storedName = await SeedTaskAsync(RunStatus: null);
        // Con la carpeta de solo lectura File.Delete lanza UnauthorizedAccessException.
        File.SetUnixFileMode(_StorageDirectory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        if (!CanNotDelete(storedName)) return; // Ejecutando como root el permiso no se aplica.

        await using var db = CreateDbContext();
        var repository = CreateRepository(db);
        var task = await db.Tasks.SingleAsync();

        await repository.DeleteAsync(
            Item: task,
            Actor: "tester",
            CT: CancellationToken.None);

        await using var check = CreateDbContext();
        Assert.Equal(0, await check.Tasks.CountAsync());
        Assert.True(File.Exists(Path.Combine(_StorageDirectory, storedName)));
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

    // ------------------------------------------------------------------ UpdateAsync

    private async Task<int> SeedEditableTaskAsync()
    {
        await using var db = CreateDbContext();
        db.Projects.Add(new Project { ID = 1, Name = "Proyecto", RepoOwner = "o", RepoName = "r" });
        db.Stages.Add(new Stage { ID = 1, Name = "Backlog", Kind = StageKind.MANUAL });
        db.Workers.Add(new WorkerNode { ID = 5, Name = "PC-1", KeyHash = "h" });
        var task = NewTask("Original");
        task.Description = "Descripción original";
        db.Tasks.Add(task);
        await db.SaveChangesAsync();
        return task.ID;
    }

    private static TaskUpdateViewModel UpdateRecord(
        string? Title = "Nuevo título", string? Description = "Nueva descripción", string? Labels = "api, ui, API",
        string? ModelOverride = " opus ", int? PreferredWorkerID = 5,
        Priority Priority = Priority.HIGH, Complexity Complexity = Complexity.MEDIUM) =>
        new(Priority, Complexity, Labels, ModelOverride, PreferredWorkerID, Title, Description);

    private async Task<TaskItem> UpdateAsync(int TaskID, TaskUpdateViewModel Record)
    {
        await using var db = CreateDbContext();
        var task = await db.Tasks.SingleAsync(t => t.ID == TaskID);
        await CreateRepository(db).UpdateAsync(
            Item: task,
            Record: Record,
            Actor: "tester",
            CT: CancellationToken.None);
        return task;
    }

    [Fact]
    public async Task UpdateAsync_ValidRecord_UpdatesTheFieldsAndLogsAnEvent()
    {
        var taskID = await SeedEditableTaskAsync();

        await UpdateAsync(taskID, UpdateRecord());

        await using var check = CreateDbContext();
        var task = await check.Tasks.SingleAsync();
        Assert.Equal("Nuevo título", task.Title);
        Assert.Equal("Nueva descripción", task.Description);
        Assert.Equal(Priority.HIGH, task.Priority);
        Assert.Equal(Complexity.MEDIUM, task.Complexity);
        Assert.Equal("api, ui", task.Labels);
        Assert.Equal("opus", task.ModelOverride);
        Assert.Equal(5, task.PreferredWorkerID);
        Assert.Contains("tester editó la tarea", (await check.TaskEvents.SingleAsync()).Message);
    }

    [Fact]
    public async Task UpdateAsync_NullTitleAndDescription_KeepTheStoredValues()
    {
        var taskID = await SeedEditableTaskAsync();

        await UpdateAsync(taskID, UpdateRecord(Title: null, Description: null));

        await using var check = CreateDbContext();
        var task = await check.Tasks.SingleAsync();
        Assert.Equal("Original", task.Title);
        Assert.Equal("Descripción original", task.Description);
    }

    [Fact]
    public async Task UpdateAsync_BlankOptionalFields_ClearThem()
    {
        var taskID = await SeedEditableTaskAsync();

        await UpdateAsync(taskID, UpdateRecord(
            Labels: "  ",
            ModelOverride: "  ",
            PreferredWorkerID: null));

        await using var check = CreateDbContext();
        var task = await check.Tasks.SingleAsync();
        Assert.Null(task.Labels);
        Assert.Null(task.ModelOverride);
        Assert.Null(task.PreferredWorkerID);
    }

    [Fact]
    public async Task UpdateAsync_TitleLongerThanTheColumn_IsTruncated()
    {
        var taskID = await SeedEditableTaskAsync();

        await UpdateAsync(taskID, UpdateRecord(Title: new string('t', 500)));

        await using var check = CreateDbContext();
        Assert.Equal(200, (await check.Tasks.SingleAsync()).Title.Length);
    }

    [Fact]
    public async Task UpdateAsync_ModelOverrideLongerThanTheColumn_ThrowsValidationInsteadOfFailingInTheDatabase()
    {
        var taskID = await SeedEditableTaskAsync();

        var ex = await Assert.ThrowsAsync<ValidationFailedException>(() => UpdateAsync(taskID, UpdateRecord(ModelOverride: new string('m', 101))));

        Assert.Contains("100", ex.Message);
        await using var check = CreateDbContext();
        Assert.Equal("Original", (await check.Tasks.SingleAsync()).Title);
        Assert.Equal(0, await check.TaskEvents.CountAsync());
    }

    [Theory]
    [InlineData("modelo con espacios")]
    [InlineData("--flag")]
    [InlineData("a;b")]
    public async Task UpdateAsync_ModelOverrideWithInvalidCharacters_Throws(string ModelOverride)
    {
        var taskID = await SeedEditableTaskAsync();

        await Assert.ThrowsAsync<ValidationFailedException>(() => UpdateAsync(taskID, UpdateRecord(ModelOverride: ModelOverride)));
    }

    [Fact]
    public async Task UpdateAsync_DescriptionOverTheLimit_Throws()
    {
        var taskID = await SeedEditableTaskAsync();

        await Assert.ThrowsAsync<ValidationFailedException>(() => UpdateAsync(taskID, UpdateRecord(Description: new string('d', 50_001))));
    }

    [Fact]
    public async Task UpdateAsync_UndefinedPriority_Throws()
    {
        var taskID = await SeedEditableTaskAsync();

        var ex = await Assert.ThrowsAsync<ValidationFailedException>(() => UpdateAsync(taskID, UpdateRecord(Priority: (Priority)99)));

        Assert.Equal(nameof(TaskUpdateViewModel.Priority), ex.Errors.Single().Field);
    }

    [Fact]
    public async Task UpdateAsync_UndefinedComplexity_Throws()
    {
        var taskID = await SeedEditableTaskAsync();

        await Assert.ThrowsAsync<ValidationFailedException>(() => UpdateAsync(taskID, UpdateRecord(Complexity: (Complexity)99)));
    }

    [Fact]
    public async Task UpdateAsync_UnknownWorker_Throws()
    {
        var taskID = await SeedEditableTaskAsync();

        var ex = await Assert.ThrowsAsync<ValidationFailedException>(() => UpdateAsync(taskID, UpdateRecord(PreferredWorkerID: 999)));

        Assert.Contains("PC no existe", ex.Message);
    }

    // ------------------------------------------------------------------ lecturas

    [Theory]
    [InlineData("abc", "%abc%")]
    [InlineData("100%", @"%100\%%")]
    [InlineData("a_b", @"%a\_b%")]
    [InlineData(@"a\b", @"%a\\b%")]
    public void BuildLikePattern_EscapesWildcards(string Term, string Expected) =>
        Assert.Equal(Expected, TasksRepository.BuildLikePattern(Term));

    [Fact]
    public async Task NewCreateAsync_WithoutProjects_ReturnsNull()
    {
        await using var db = CreateDbContext();

        var record = await CreateRepository(db).NewCreateAsync(
            ProjectID: null,
            CT: CancellationToken.None);

        Assert.Null(record);
    }

    [Fact]
    public async Task NewCreateAsync_UsesTheRequestedProjectOrFallsBackToTheFirstActiveByName()
    {
        await using (var db = CreateDbContext())
        {
            db.Projects.AddRange(
                new Project { ID = 1, Name = "Zeta", RepoOwner = "o", RepoName = "z", DefaultBranch = "main" },
                new Project { ID = 2, Name = "Alfa", RepoOwner = "o", RepoName = "a", DefaultBranch = "develop" },
                new Project { ID = 3, Name = "Apagado", RepoOwner = "o", RepoName = "x", IsActive = false });
            await db.SaveChangesAsync();
        }

        await using var check = CreateDbContext();
        var repository = CreateRepository(check);

        var requested = await repository.NewCreateAsync(
            ProjectID: 1,
            CT: CancellationToken.None);
        var inactive = await repository.NewCreateAsync(
            ProjectID: 3,
            CT: CancellationToken.None);
        var unknown = await repository.NewCreateAsync(
            ProjectID: 999,
            CT: CancellationToken.None);

        Assert.Equal(1, requested!.ProjectID);
        Assert.Equal("main", requested.BaseBranch);
        Assert.Equal(2, inactive!.ProjectID);
        Assert.Equal("develop", unknown!.BaseBranch);
    }

    private async Task SeedBoardAsync()
    {
        await using var db = CreateDbContext();
        db.Projects.AddRange(
            new Project { ID = 1, Name = "P1", RepoOwner = "o", RepoName = "a" },
            new Project { ID = 2, Name = "P2", RepoOwner = "o", RepoName = "b" });
        db.Stages.AddRange(
            new Stage { ID = 1, Name = "Backlog", Order = 10, Kind = StageKind.MANUAL, SuccessStageID = 2 },
            new Stage { ID = 2, Name = "Desarrollador", Order = 20, Kind = StageKind.AGENT, PushChanges = true, RejectStageID = 1 });
        await db.SaveChangesAsync();
        db.Tasks.AddRange(
            new TaskItem { ID = 1, Title = "Baja", ProjectID = 1, StageID = 1, Priority = Priority.LOW, CreatedByName = "t" },
            new TaskItem { ID = 2, Title = "Crítica", ProjectID = 1, StageID = 1, Priority = Priority.CRITICAL, CreatedByName = "t" },
            new TaskItem { ID = 3, Title = "Otro proyecto", ProjectID = 2, StageID = 1, CreatedByName = "t" },
            new TaskItem { ID = 4, Title = "Archivada", ProjectID = 1, StageID = 1, ArchivedAt = DateTimeOffset.UtcNow, CreatedByName = "t" },
            new TaskItem { ID = 5, Title = "En espera", ProjectID = 1, StageID = 1, Status = WorkStatus.WAITING_APPROVAL, CreatedByName = "t" },
            new TaskItem { ID = 6, Title = "Programando", ProjectID = 1, StageID = 2, Status = WorkStatus.RUNNING, LastWorkerName = "PC-1", CreatedByName = "t" });
        await db.SaveChangesAsync();
        db.TaskRuns.Add(new TaskRun
        {
            TaskID = 6, StageID = 2, Status = RunStatus.RUNNING, StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
        });
        await db.SaveChangesAsync();
    }

    private async Task<BoardViewModel> LoadBoardAsync(BoardFilterViewModel Filter)
    {
        await using var db = CreateDbContext();
        return await CreateRepository(db).GetBoardAsync(
            Filter: Filter,
            CT: CancellationToken.None);
    }

    private static List<string> TitlesOf(BoardViewModel Board) =>
        Board.Columns.SelectMany(c => c.Tasks).Select(c => c.Item.Title).Order().ToList();

    [Fact]
    public async Task GetBoardAsync_WithoutFilters_ShowsActiveTasksInEveryColumnOrderedByPriority()
    {
        await SeedBoardAsync();

        var board = await LoadBoardAsync(new BoardFilterViewModel());

        Assert.Equal(["Backlog", "Desarrollador"], board.Columns.Select(c => c.Stage.Name));
        Assert.Equal("Crítica", board.Columns[0].Tasks[0].Item.Title);
        Assert.DoesNotContain("Archivada", TitlesOf(board));
        Assert.Equal(5, TitlesOf(board).Count);
    }

    [Fact]
    public async Task GetBoardAsync_ColumnsKnowTheNamesOfTheirDestinations()
    {
        await SeedBoardAsync();

        var board = await LoadBoardAsync(new BoardFilterViewModel());

        Assert.Equal("Desarrollador", board.Columns[0].SuccessName);
        Assert.Null(board.Columns[0].RejectName);
        Assert.Equal("Backlog", board.Columns[1].RejectName);
    }

    [Fact]
    public async Task GetBoardAsync_ArchivedFilter_ShowsOnlyArchivedTasks()
    {
        await SeedBoardAsync();

        var board = await LoadBoardAsync(new BoardFilterViewModel { Archived = true });

        Assert.Equal(["Archivada"], TitlesOf(board));
    }

    [Fact]
    public async Task GetBoardAsync_ProjectFilter_ShowsOnlyThatProject()
    {
        await SeedBoardAsync();

        var board = await LoadBoardAsync(new BoardFilterViewModel { ProjectID = 2 });

        Assert.Equal(["Otro proyecto"], TitlesOf(board));
    }

    [Fact]
    public async Task GetBoardAsync_WaitingFilter_ShowsOnlyTasksWaitingForApproval()
    {
        await SeedBoardAsync();

        var board = await LoadBoardAsync(new BoardFilterViewModel { Waiting = true });

        Assert.Equal(["En espera"], TitlesOf(board));
    }

    [Fact]
    public async Task GetBoardAsync_WorkerFilter_ShowsOnlyTasksOfThatWorker()
    {
        await SeedBoardAsync();

        var board = await LoadBoardAsync(new BoardFilterViewModel { WorkerName = "PC-1" });

        Assert.Equal(["Programando"], TitlesOf(board));
    }

    [Fact]
    public async Task GetBoardAsync_TaskBeingDeveloped_ShowsTheElapsedTimeAndCountsIt()
    {
        await SeedBoardAsync();

        var board = await LoadBoardAsync(new BoardFilterViewModel());

        var card = board.Columns[1].Tasks.Single();
        Assert.NotNull(card.DevelopmentElapsed);
        Assert.InRange(card.DevelopmentElapsed.Value.TotalMinutes, 4.9, 6);
        Assert.Equal(1, board.DevelopingCount);
        Assert.All(board.Columns[0].Tasks, c => Assert.Null(c.DevelopmentElapsed));
    }

    [Fact]
    public async Task GetBoardAsync_LongDescriptions_AreCutForTheCardsAndShortOnesStayWhole()
    {
        await SeedBoardAsync();
        await using (var db = CreateDbContext())
        {
            (await db.Tasks.SingleAsync(t => t.ID == 1)).Description = new string('d', 5000);
            (await db.Tasks.SingleAsync(t => t.ID == 2)).Description = "corta";
            await db.SaveChangesAsync();
        }

        var board = await LoadBoardAsync(new BoardFilterViewModel());

        var cards = board.Columns.SelectMany(c => c.Tasks).ToDictionary(c => c.Item.ID);
        Assert.Equal(1000, cards[1].Item.Description.Length);
        Assert.Equal("corta", cards[2].Item.Description);
    }

    [Fact]
    public async Task GetBoardAsync_CardsCarryEverythingTheViewShows()
    {
        await SeedBoardAsync();
        await using (var db = CreateDbContext())
        {
            var task = await db.Tasks.SingleAsync(t => t.ID == 6);
            task.Labels = "api";
            task.WorkBranch = "devarv/6-x";
            task.PullRequestNumber = 9;
            task.PullRequestUrl = "https://github.com/o/a/pull/9";
            task.TotalCostUsd = 1.25m;
            task.TotalTokens = 321;
            await db.SaveChangesAsync();
        }

        var board = await LoadBoardAsync(new BoardFilterViewModel());

        var item = board.Columns[1].Tasks.Single().Item;
        Assert.Equal("P1", item.Project.Name);
        Assert.Equal("api", item.Labels);
        Assert.Equal("devarv/6-x", item.WorkBranch);
        Assert.Equal(9, item.PullRequestNumber);
        Assert.Equal("https://github.com/o/a/pull/9", item.PullRequestUrl);
        Assert.Equal(1.25m, item.TotalCostUsd);
        Assert.Equal(321, item.TotalTokens);
        Assert.Equal("PC-1", item.LastWorkerName);
    }

    [Fact]
    public async Task GetBoardAsync_OrdersEachColumnByPriority_CriticalFirst()
    {
        await using (var db = CreateDbContext())
        {
            db.Projects.Add(new Project { ID = 1, Name = "P", RepoOwner = "o", RepoName = "r" });
            db.Stages.Add(new Stage { ID = 1, Name = "Backlog", Order = 10 });
            await db.SaveChangesAsync();
            // Se insertan en un orden que no coincide con la prioridad ni con el alfabético.
            foreach (var priority in new[] { Priority.LOW, Priority.CRITICAL, Priority.MEDIUM, Priority.HIGH })
                db.Tasks.Add(new TaskItem { Title = priority.ToString(), ProjectID = 1, StageID = 1, Priority = priority, CreatedByName = "t" });
            await db.SaveChangesAsync();
        }

        var board = await LoadBoardAsync(new BoardFilterViewModel());

        Assert.Equal(["CRITICAL", "HIGH", "MEDIUM", "LOW"], board.Columns[0].Tasks.Select(c => c.Item.Title));
    }

    [Fact]
    public async Task LockSql_ReferencesTheTablesOfTheModel()
    {
        await using var db = CreateDbContext();

        Assert.Contains($"\"{db.Model.FindEntityType(typeof(TaskItem))!.GetTableName()}\"", TasksRepository.LOCK_TASK_SQL);
        Assert.Contains($"\"{db.Model.FindEntityType(typeof(TaskRun))!.GetTableName()}\"", TasksRepository.LOCK_RUNS_SQL);
        // Las columnas conservan su nombre histórico.
        Assert.Contains("\"TaskId\"", TasksRepository.LOCK_RUNS_SQL);
        Assert.DoesNotContain("TaskID", TasksRepository.LOCK_RUNS_SQL);
    }

    [Fact]
    public async Task GetBoardAsync_TheBoardFlagsAreCopiedFromTheFilter()
    {
        await SeedBoardAsync();

        var board = await LoadBoardAsync(new BoardFilterViewModel { Compact = true, HideEmpty = true });

        Assert.True(board.Compact);
        Assert.True(board.HideEmpty);
    }

    [Fact]
    public async Task CountWaitingAsync_CountsTasksWaitingForApprovalThatAreNotArchived()
    {
        await SeedBoardAsync();
        await using var db = CreateDbContext();

        Assert.Equal(1, await CreateRepository(db).CountWaitingAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetDetailsViewAsync_UnknownTask_ReturnsNull()
    {
        await using var db = CreateDbContext();

        var view = await CreateRepository(db).GetDetailsViewAsync(
            ID: 999,
            RunID: null,
            CT: CancellationToken.None);

        Assert.Null(view);
    }

    [Fact]
    public async Task GetDetailsViewAsync_SelectsTheRequestedRunOrTheMostRecentOne()
    {
        var taskID = await SeedEditableTaskAsync();
        await using (var db = CreateDbContext())
        {
            db.TaskRuns.AddRange(
                new TaskRun { ID = 10, TaskID = taskID, StageID = 1, ChangedFilesJson = """[{"Status":"M","Path":"a.cs"}]""" },
                new TaskRun { ID = 11, TaskID = taskID, StageID = 1, ChangedFilesJson = "no es json" });
            await db.SaveChangesAsync();
        }

        await using var check = CreateDbContext();
        var repository = CreateRepository(check);
        var latest = await repository.GetDetailsViewAsync(
            ID: taskID,
            RunID: null,
            CT: CancellationToken.None);
        var requested = await repository.GetDetailsViewAsync(
            ID: taskID,
            RunID: 10,
            CT: CancellationToken.None);

        Assert.Equal(11, latest!.SelectedRun!.ID);
        Assert.Empty(latest.SelectedRunFiles);
        Assert.Equal(10, requested!.SelectedRun!.ID);
        Assert.Single(requested.SelectedRunFiles);
        Assert.Equal(["Original"], [requested.Item.Title]);
        Assert.Single(requested.Workers);
    }

    private bool CanNotDelete(string StoredName)
    {
        try
        {
            File.Delete(Path.Combine(_StorageDirectory, StoredName));
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    private AppDbContext CreateDbContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(_DatabaseName)
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options);

    private TasksRepository CreateRepository(AppDbContext Db)
    {
        // StoragePath es absoluta, así que AttachmentStorage no usa el IWebHostEnvironment.
        var storage = new AttachmentStorage(Options.Create(new AttachmentOptions { StoragePath = _StorageDirectory }), null!);
        return new TestableTasksRepository(Db, storage);
    }

    private static TaskItem NewTask(string Title) => new()
    {
        Title = Title,
        ProjectID = 1,
        StageID = 1,
        CreatedByName = "tester",
    };

    /// <summary>Crea proyecto, columna y una tarea con un adjunto en disco; devuelve el nombre del archivo guardado.</summary>
    private async Task<string> SeedTaskAsync(RunStatus? RunStatus)
    {
        var storedName = $"{Guid.NewGuid():N}.png";
        await File.WriteAllBytesAsync(Path.Combine(_StorageDirectory, storedName), [1, 2, 3]);

        await using var db = CreateDbContext();
        db.Projects.Add(new Project { ID = 1, Name = "Proyecto", RepoOwner = "o", RepoName = "r" });
        db.Stages.Add(new Stage { ID = 1, Name = "Backlog", Kind = StageKind.MANUAL });
        var task = NewTask("Tarea");
        task.Attachments.Add(new TaskAttachment { FileName = "a.png", StoredName = storedName, Kind = AttachmentKind.IMAGE });
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        if (RunStatus is not null)
        {
            db.TaskRuns.Add(new TaskRun { TaskID = task.ID, StageID = 1, Status = RunStatus.Value });
            await db.SaveChangesAsync();
        }
        return storedName;
    }

    /// <summary>El proveedor en memoria no ejecuta SQL, así que se omiten los bloqueos FOR UPDATE de PostgreSQL.</summary>
    private sealed class TestableTasksRepository(AppDbContext Db, AttachmentStorage Storage)
        : TasksRepository(Db, Storage, NullLogger<TasksRepository>.Instance)
    {
        protected override Task LockTaskAndRunsAsync(int TaskID, CancellationToken CT) => Task.CompletedTask;
    }
}
