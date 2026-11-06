using DevArv.Web.DatabaseConnection;
using DevArv.Web.DataModels;
using DevArv.Web.Enums.Runs;
using DevArv.Web.Enums.Tasks;
using DevArv.Web.Enums.Workflow;
using DevArv.Web.Models;
using DevArv.Web.Repositories;
using DevArv.Web.Services;
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
        var storedName = await SeedTaskAsync(RunStatus.Running);

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
        Assert.True(File.Exists(Path.Combine(_StorageDirectory, storedName)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(RunStatus.Queued)]
    [InlineData(RunStatus.Succeeded)]
    [InlineData(RunStatus.Failed)]
    [InlineData(RunStatus.Cancelled)]
    public async Task DeleteAsync_WithoutRunningRun_DeletesTaskAttachmentsAndFiles(RunStatus? runStatus)
    {
        var storedName = await SeedTaskAsync(runStatus);

        await using var db = CreateDbContext();
        var repository = CreateRepository(db);
        var task = await db.Tasks.SingleAsync();

        await repository.DeleteAsync(
            task: task,
            actor: "tester",
            ct: CancellationToken.None);

        await using var check = CreateDbContext();
        Assert.Equal(0, await check.Tasks.CountAsync());
        Assert.Equal(0, await check.TaskAttachments.CountAsync());
        Assert.False(File.Exists(Path.Combine(_StorageDirectory, storedName)));
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
    public async Task DeleteAsync_WhenFileCannotBeDeleted_StillDeletesTaskAndTriesRemainingFiles()
    {
        if (OperatingSystem.IsWindows()) return; // El bloqueo se simula con permisos POSIX de la carpeta.

        var storedName = await SeedTaskAsync(runStatus: null);
        // Con la carpeta de solo lectura File.Delete lanza UnauthorizedAccessException.
        File.SetUnixFileMode(_StorageDirectory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        if (!CanNotDelete(storedName)) return; // Ejecutando como root el permiso no se aplica.

        await using var db = CreateDbContext();
        var repository = CreateRepository(db);
        var task = await db.Tasks.SingleAsync();

        await repository.DeleteAsync(
            task: task,
            actor: "tester",
            ct: CancellationToken.None);

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

    private bool CanNotDelete(string storedName)
    {
        try
        {
            File.Delete(Path.Combine(_StorageDirectory, storedName));
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

    private TasksRepository CreateRepository(AppDbContext db)
    {
        // StoragePath es absoluta, así que AttachmentStorage no usa el IWebHostEnvironment.
        var storage = new AttachmentStorage(Options.Create(new AttachmentOptions { StoragePath = _StorageDirectory }), null!);
        return new TestableTasksRepository(db, storage);
    }

    private static TaskItem NewTask(string title) => new()
    {
        Title = title,
        ProjectId = 1,
        StageId = 1,
        CreatedByName = "tester",
    };

    /// <summary>Crea proyecto, columna y una tarea con un adjunto en disco; devuelve el nombre del archivo guardado.</summary>
    private async Task<string> SeedTaskAsync(RunStatus? runStatus)
    {
        var storedName = $"{Guid.NewGuid():N}.png";
        await File.WriteAllBytesAsync(Path.Combine(_StorageDirectory, storedName), [1, 2, 3]);

        await using var db = CreateDbContext();
        db.Projects.Add(new Project { Id = 1, Name = "Proyecto", RepoOwner = "o", RepoName = "r" });
        db.Stages.Add(new Stage { Id = 1, Name = "Backlog", Kind = StageKind.Manual });
        var task = NewTask("Tarea");
        task.Attachments.Add(new TaskAttachment { FileName = "a.png", StoredName = storedName, Kind = AttachmentKind.Image });
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        if (runStatus is not null)
        {
            db.TaskRuns.Add(new TaskRun { TaskId = task.Id, StageId = 1, Status = runStatus.Value });
            await db.SaveChangesAsync();
        }
        return storedName;
    }

    /// <summary>El proveedor en memoria no ejecuta SQL, así que se omiten los bloqueos FOR UPDATE de PostgreSQL.</summary>
    private sealed class TestableTasksRepository(AppDbContext db, AttachmentStorage storage)
        : TasksRepository(db, storage, NullLogger<TasksRepository>.Instance)
    {
        protected override Task LockTaskAndRunsAsync(int taskId, CancellationToken ct) => Task.CompletedTask;
    }
}
