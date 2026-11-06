using DevArv.Tests.TestSupport;
using DevArv.Web.DataModels;
using DevArv.Web.DatabaseConnection;
using DevArv.Web.Enums.Runs;
using DevArv.Web.Enums.Tasks;
using DevArv.Web.Models;
using DevArv.Web.Repositories;
using DevArv.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DevArv.Tests.Web.Repositories;

public sealed class TaskAttachmentsRepositoryTests : IDisposable
{
    private readonly string _DatabaseName = Guid.NewGuid().ToString("N");
    private readonly string _StorageDirectory = Path.Combine(Path.GetTempPath(), $"devarv-attachments-{Guid.NewGuid():N}");

    public TaskAttachmentsRepositoryTests() => Directory.CreateDirectory(_StorageDirectory);

    public void Dispose()
    {
        if (!OperatingSystem.IsWindows() && Directory.Exists(_StorageDirectory))
            File.SetUnixFileMode(_StorageDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        if (Directory.Exists(_StorageDirectory)) Directory.Delete(_StorageDirectory, recursive: true);
    }

    private AppDbContext CreateDbContext() => TestDatabase.CreateContext(_DatabaseName);

    private AttachmentStorage CreateStorage(int MaxFilesPerTask = 15) =>
        new(Options.Create(new AttachmentOptions { StoragePath = _StorageDirectory, MaxFilesPerTask = MaxFilesPerTask }), null!);

    private TaskAttachmentsRepository CreateRepository(AppDbContext Db, int MaxFilesPerTask = 15) =>
        new(Db, CreateStorage(MaxFilesPerTask), NullLogger<TaskAttachmentsRepository>.Instance);

    private string[] FilesOnDisk() => Directory.GetFiles(_StorageDirectory).Select(Path.GetFileName).ToArray()!;

    private async Task<int> SeedTaskAsync()
    {
        await using var db = CreateDbContext();
        db.Projects.Add(new Project { Id = 1, Name = "P", RepoOwner = "o", RepoName = "r" });
        db.Stages.Add(new Stage { Id = 1, Name = "S" });
        await db.SaveChangesAsync();
        var task = new TaskItem { Title = "T", ProjectId = 1, StageId = 1 };
        db.Tasks.Add(task);
        await db.SaveChangesAsync();
        return task.Id;
    }

    private async Task<(int TaskID, TaskAttachment Attachment)> SeedAttachmentAsync()
    {
        var taskID = await SeedTaskAsync();
        var storedName = $"{Guid.NewGuid():N}.png";
        await File.WriteAllBytesAsync(Path.Combine(_StorageDirectory, storedName), TestFiles.PngBytes);
        await using var db = CreateDbContext();
        var attachment = new TaskAttachment
        {
            TaskId = taskID, FileName = "a.png", StoredName = storedName, ContentType = "image/png", Kind = AttachmentKind.Image,
        };
        db.TaskAttachments.Add(attachment);
        await db.SaveChangesAsync();
        return (taskID, attachment);
    }

    // ------------------------------------------------------------------ AddAsync

    [Fact]
    public async Task AddAsync_WithoutFiles_Throws()
    {
        var taskID = await SeedTaskAsync();
        await using var db = CreateDbContext();

        var ex = await Assert.ThrowsAsync<ValidationFailedException>(() => CreateRepository(db).AddAsync(
            TaskID: taskID,
            Files: [],
            Actor: "tester",
            CT: CancellationToken.None));

        Assert.Contains("al menos un archivo", ex.Message);
        await using var check = CreateDbContext();
        Assert.Equal(0, await check.TaskEvents.CountAsync());
    }

    [Fact]
    public async Task AddAsync_InvalidFile_ThrowsValidationAndStoresNothing()
    {
        var taskID = await SeedTaskAsync();
        await using var db = CreateDbContext();
        var file = TestFiles.Create("virus.exe", [1, 2, 3, 4]);

        var ex = await Assert.ThrowsAsync<ValidationFailedException>(() => CreateRepository(db).AddAsync(
            TaskID: taskID,
            Files: [file],
            Actor: "tester",
            CT: CancellationToken.None));

        Assert.Contains("no es un formato permitido", ex.Message);
        Assert.Empty(FilesOnDisk());
        await using var check = CreateDbContext();
        Assert.Equal(0, await check.TaskAttachments.CountAsync());
    }

    [Fact]
    public async Task AddAsync_MoreThanTheLimit_Throws()
    {
        var taskID = await SeedTaskAsync();
        await using var db = CreateDbContext();

        var ex = await Assert.ThrowsAsync<ValidationFailedException>(() => CreateRepository(db, MaxFilesPerTask: 1).AddAsync(
            TaskID: taskID,
            Files: [TestFiles.CreatePng("a.png"), TestFiles.CreatePng("b.png")],
            Actor: "tester",
            CT: CancellationToken.None));

        Assert.Contains("Máximo 1", ex.Message);
        Assert.Empty(FilesOnDisk());
    }

    [Fact]
    public async Task AddAsync_ValidFiles_PersistsAttachmentsEventAndFiles()
    {
        var taskID = await SeedTaskAsync();
        await using var db = CreateDbContext();

        await CreateRepository(db).AddAsync(
            TaskID: taskID,
            Files: [TestFiles.CreatePng("uno.png"), TestFiles.CreatePng("dos.png")],
            Actor: "tester",
            CT: CancellationToken.None);

        await using var check = CreateDbContext();
        var attachments = await check.TaskAttachments.OrderBy(a => a.FileName).ToListAsync();
        Assert.Equal(["dos.png", "uno.png"], attachments.Select(a => a.FileName));
        Assert.All(attachments, a => Assert.Equal(AttachmentKind.Image, a.Kind));
        Assert.Equal(2, FilesOnDisk().Length);
        Assert.Contains("adjuntó 2 archivo(s)", (await check.TaskEvents.SingleAsync()).Message);
    }

    [Fact]
    public async Task AddAsync_WhenTheDatabaseRejectsIt_DeletesTheFilesAlreadyWritten()
    {
        var taskID = await SeedTaskAsync();
        await using var db = new FailingDbContext(TestDatabase.CreateOptions(_DatabaseName), FailOnSave: 1);

        await Assert.ThrowsAsync<DbUpdateException>(() => CreateRepository(db).AddAsync(
            TaskID: taskID,
            Files: [TestFiles.CreatePng()],
            Actor: "tester",
            CT: CancellationToken.None));

        Assert.Empty(FilesOnDisk());
    }

    // ------------------------------------------------------------------ DeleteAsync

    [Fact]
    public async Task DeleteAsync_ExistingAttachment_RemovesRecordAndFileAndLogsTheEvent()
    {
        var (taskID, attachment) = await SeedAttachmentAsync();
        await using var db = CreateDbContext();

        await CreateRepository(db).DeleteAsync(
            TaskID: taskID,
            AttachmentID: attachment.Id,
            Actor: "tester",
            CT: CancellationToken.None);

        await using var check = CreateDbContext();
        Assert.Equal(0, await check.TaskAttachments.CountAsync());
        Assert.Empty(FilesOnDisk());
        Assert.Contains("eliminó el adjunto «a.png»", (await check.TaskEvents.SingleAsync()).Message);
    }

    [Fact]
    public async Task DeleteAsync_AttachmentOfAnotherTask_Throws()
    {
        var (taskID, attachment) = await SeedAttachmentAsync();
        await using var db = CreateDbContext();

        await Assert.ThrowsAsync<ValidationFailedException>(() => CreateRepository(db).DeleteAsync(
            TaskID: taskID + 1,
            AttachmentID: attachment.Id,
            Actor: "tester",
            CT: CancellationToken.None));

        await using var check = CreateDbContext();
        Assert.Equal(1, await check.TaskAttachments.CountAsync());
        Assert.Single(FilesOnDisk());
    }

    [Fact]
    public async Task DeleteAsync_WhenTheFileCannotBeDeleted_StillSucceeds()
    {
        if (OperatingSystem.IsWindows()) return; // El bloqueo se simula con permisos POSIX de la carpeta.

        var (taskID, attachment) = await SeedAttachmentAsync();
        File.SetUnixFileMode(_StorageDirectory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            File.Delete(Path.Combine(_StorageDirectory, attachment.StoredName));
            return; // Ejecutando como root el permiso no se aplica.
        }
        catch (UnauthorizedAccessException)
        {
        }

        await using var db = CreateDbContext();
        await CreateRepository(db).DeleteAsync(
            TaskID: taskID,
            AttachmentID: attachment.Id,
            Actor: "tester",
            CT: CancellationToken.None);

        await using var check = CreateDbContext();
        Assert.Equal(0, await check.TaskAttachments.CountAsync());
    }

    // ------------------------------------------------------------------ lectura de archivos

    [Fact]
    public async Task GetFileAsync_ReturnsTheAttachmentAndItsPath()
    {
        var (taskID, attachment) = await SeedAttachmentAsync();
        await using var db = CreateDbContext();

        var file = await CreateRepository(db).GetFileAsync(
            TaskID: taskID,
            AttachmentID: attachment.Id,
            CT: CancellationToken.None);

        Assert.NotNull(file);
        Assert.Equal(Path.Combine(_StorageDirectory, attachment.StoredName), file.Value.Path);
    }

    [Fact]
    public async Task GetFileAsync_AttachmentOfAnotherTask_ReturnsNull()
    {
        var (taskID, attachment) = await SeedAttachmentAsync();
        await using var db = CreateDbContext();

        var file = await CreateRepository(db).GetFileAsync(
            TaskID: taskID + 1,
            AttachmentID: attachment.Id,
            CT: CancellationToken.None);

        Assert.Null(file);
    }

    [Fact]
    public async Task GetFileAsync_MissingFileOnDisk_ReturnsNull()
    {
        var (taskID, attachment) = await SeedAttachmentAsync();
        File.Delete(Path.Combine(_StorageDirectory, attachment.StoredName));
        await using var db = CreateDbContext();

        var file = await CreateRepository(db).GetFileAsync(
            TaskID: taskID,
            AttachmentID: attachment.Id,
            CT: CancellationToken.None);

        Assert.Null(file);
    }

    private async Task<long> SeedRunAsync(int TaskID, int WorkerID, RunStatus Status)
    {
        await using var db = CreateDbContext();
        var run = new TaskRun { TaskId = TaskID, StageId = 1, WorkerId = WorkerID, Status = Status };
        db.TaskRuns.Add(run);
        await db.SaveChangesAsync();
        return run.Id;
    }

    [Fact]
    public async Task GetFileForWorkerRunAsync_RunningRunOfThatWorker_ReturnsTheFile()
    {
        var (taskID, attachment) = await SeedAttachmentAsync();
        var runID = await SeedRunAsync(taskID, WorkerID: 7, RunStatus.Running);
        await using var db = CreateDbContext();

        var file = await CreateRepository(db).GetFileForWorkerRunAsync(
            RunID: runID,
            AttachmentID: attachment.Id,
            WorkerID: 7,
            CT: CancellationToken.None);

        Assert.NotNull(file);
    }

    [Fact]
    public async Task GetFileForWorkerRunAsync_RunOfAnotherWorker_ReturnsNull()
    {
        var (taskID, attachment) = await SeedAttachmentAsync();
        var runID = await SeedRunAsync(taskID, WorkerID: 7, RunStatus.Running);
        await using var db = CreateDbContext();

        var file = await CreateRepository(db).GetFileForWorkerRunAsync(
            RunID: runID,
            AttachmentID: attachment.Id,
            WorkerID: 8,
            CT: CancellationToken.None);

        Assert.Null(file);
    }

    [Fact]
    public async Task GetFileForWorkerRunAsync_RunNoLongerRunning_ReturnsNull()
    {
        var (taskID, attachment) = await SeedAttachmentAsync();
        var runID = await SeedRunAsync(taskID, WorkerID: 7, RunStatus.Succeeded);
        await using var db = CreateDbContext();

        var file = await CreateRepository(db).GetFileForWorkerRunAsync(
            RunID: runID,
            AttachmentID: attachment.Id,
            WorkerID: 7,
            CT: CancellationToken.None);

        Assert.Null(file);
    }
}
