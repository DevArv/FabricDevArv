using DevArv.Tests.TestSupport;
using DevArv.Web.DataModels;
using DevArv.Web.DatabaseConnection;
using DevArv.Web.Enums.Runs;
using DevArv.Web.Enums.Tasks;
using DevArv.Web.Models;
using DevArv.Web.Repositories;
using DevArv.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DevArv.Tests.Web.Repositories;

public sealed class TaskAttachmentsRepositoryTests
{
    private readonly string _DatabaseName = Guid.NewGuid().ToString("N");

    private AppDbContext CreateDbContext() => TestDatabase.CreateContext(_DatabaseName);

    private static AttachmentStorage CreateStorage(int MaxFilesPerTask = 15) =>
        new(Options.Create(new AttachmentOptions { MaxFilesPerTask = MaxFilesPerTask }));

    private static TaskAttachmentsRepository CreateRepository(AppDbContext Db, int MaxFilesPerTask = 15) =>
        new(Db, CreateStorage(MaxFilesPerTask));

    private async Task<int> SeedTaskAsync()
    {
        await using var db = CreateDbContext();
        db.Projects.Add(new Project { ID = 1, Name = "P", RepoOwner = "o", RepoName = "r" });
        db.Stages.Add(new Stage { ID = 1, Name = "S" });
        await db.SaveChangesAsync();
        var task = new TaskItem { Title = "T", ProjectID = 1, StageID = 1 };
        db.Tasks.Add(task);
        await db.SaveChangesAsync();
        return task.ID;
    }

    private async Task<(int TaskID, TaskAttachment Attachment)> SeedAttachmentAsync()
    {
        var taskID = await SeedTaskAsync();
        await using var db = CreateDbContext();
        var attachment = new TaskAttachment
        {
            TaskID = taskID, FileName = "a.png", ContentType = "image/png", Kind = AttachmentKind.IMAGE,
            Size = TestFiles.PngBytes.Length, Content = new TaskAttachmentContent { Data = TestFiles.PngBytes },
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
        await using var check = CreateDbContext();
        Assert.Equal(0, await check.TaskAttachments.CountAsync());
    }

    [Fact]
    public async Task AddAsync_ValidFiles_PersistsAttachmentsWithTheirContentAndTheEvent()
    {
        var taskID = await SeedTaskAsync();
        await using var db = CreateDbContext();

        await CreateRepository(db).AddAsync(
            TaskID: taskID,
            Files: [TestFiles.CreatePng("uno.png"), TestFiles.CreatePng("dos.png")],
            Actor: "tester",
            CT: CancellationToken.None);

        await using var check = CreateDbContext();
        var attachments = await check.TaskAttachments.Include(a => a.Content).OrderBy(a => a.FileName).ToListAsync();
        Assert.Equal(["dos.png", "uno.png"], attachments.Select(a => a.FileName));
        Assert.All(attachments, a =>
        {
            Assert.Equal(AttachmentKind.IMAGE, a.Kind);
            Assert.Equal("image/png", a.ContentType);
            Assert.Equal(TestFiles.PngBytes.Length, a.Size);
            Assert.Equal(TestFiles.PngBytes, a.Content!.Data);
        });
        Assert.Contains("adjuntó 2 archivo(s)", (await check.TaskEvents.SingleAsync()).Message);
    }

    [Fact]
    public async Task AddAsync_WhenTheDatabaseRejectsIt_StoresNothing()
    {
        var taskID = await SeedTaskAsync();
        await using (var db = new FailingDbContext(TestDatabase.CreateOptions(_DatabaseName), FailOnSave: 1))
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => CreateRepository(db).AddAsync(
                TaskID: taskID,
                Files: [TestFiles.CreatePng()],
                Actor: "tester",
                CT: CancellationToken.None));
        }

        await using var check = CreateDbContext();
        Assert.Equal(0, await check.TaskAttachments.CountAsync());
    }

    // ------------------------------------------------------------------ DeleteAsync

    [Fact]
    public async Task DeleteAsync_ExistingAttachment_RemovesRecordAndLogsTheEvent()
    {
        var (taskID, attachment) = await SeedAttachmentAsync();
        await using var db = CreateDbContext();

        await CreateRepository(db).DeleteAsync(
            TaskID: taskID,
            AttachmentID: attachment.ID,
            Actor: "tester",
            CT: CancellationToken.None);

        await using var check = CreateDbContext();
        // El contenido está en la misma fila y se borra con ella; el proveedor en memoria no simula el table splitting.
        Assert.Equal(0, await check.TaskAttachments.CountAsync());
        Assert.Contains("eliminó el adjunto «a.png»", (await check.TaskEvents.SingleAsync()).Message);
    }

    [Fact]
    public async Task DeleteAsync_AttachmentOfAnotherTask_Throws()
    {
        var (taskID, attachment) = await SeedAttachmentAsync();
        await using var db = CreateDbContext();

        await Assert.ThrowsAsync<ValidationFailedException>(() => CreateRepository(db).DeleteAsync(
            TaskID: taskID + 1,
            AttachmentID: attachment.ID,
            Actor: "tester",
            CT: CancellationToken.None));

        await using var check = CreateDbContext();
        Assert.Equal(1, await check.TaskAttachments.CountAsync());
    }

    // ------------------------------------------------------------------ lectura de archivos

    [Fact]
    public async Task GetFileAsync_ReturnsTheAttachmentWithItsContent()
    {
        var (taskID, attachment) = await SeedAttachmentAsync();
        await using var db = CreateDbContext();

        var file = await CreateRepository(db).GetFileAsync(
            TaskID: taskID,
            AttachmentID: attachment.ID,
            CT: CancellationToken.None);

        Assert.NotNull(file);
        Assert.Equal(TestFiles.PngBytes, file.Content!.Data);
    }

    [Fact]
    public async Task GetFileAsync_AttachmentOfAnotherTask_ReturnsNull()
    {
        var (taskID, attachment) = await SeedAttachmentAsync();
        await using var db = CreateDbContext();

        var file = await CreateRepository(db).GetFileAsync(
            TaskID: taskID + 1,
            AttachmentID: attachment.ID,
            CT: CancellationToken.None);

        Assert.Null(file);
    }

    [Fact]
    public async Task GetFileAsync_AttachmentWithoutContent_ReturnsNull()
    {
        // Adjunto antiguo cuyo archivo no se encontró al moverlo del disco a la base de datos.
        var taskID = await SeedTaskAsync();
        await using (var setup = CreateDbContext())
        {
            setup.TaskAttachments.Add(new TaskAttachment { ID = 50, TaskID = taskID, FileName = "perdido.png", Kind = AttachmentKind.IMAGE });
            await setup.SaveChangesAsync();
        }
        await using var db = CreateDbContext();

        var file = await CreateRepository(db).GetFileAsync(
            TaskID: taskID,
            AttachmentID: 50,
            CT: CancellationToken.None);

        Assert.Null(file);
    }

    private async Task<long> SeedRunAsync(int TaskID, int WorkerID, RunStatus Status)
    {
        await using var db = CreateDbContext();
        var run = new TaskRun { TaskID = TaskID, StageID = 1, WorkerID = WorkerID, Status = Status };
        db.TaskRuns.Add(run);
        await db.SaveChangesAsync();
        return run.ID;
    }

    [Fact]
    public async Task GetFileForWorkerRunAsync_RunningRunOfThatWorker_ReturnsTheFileWithItsContent()
    {
        var (taskID, attachment) = await SeedAttachmentAsync();
        var runID = await SeedRunAsync(
            TaskID: taskID,
            WorkerID: 7,
            Status: RunStatus.RUNNING);
        await using var db = CreateDbContext();

        var file = await CreateRepository(db).GetFileForWorkerRunAsync(
            RunID: runID,
            AttachmentID: attachment.ID,
            WorkerID: 7,
            CT: CancellationToken.None);

        Assert.NotNull(file);
        Assert.Equal(TestFiles.PngBytes, file.Content!.Data);
    }

    [Fact]
    public async Task GetFileForWorkerRunAsync_RunOfAnotherWorker_ReturnsNull()
    {
        var (taskID, attachment) = await SeedAttachmentAsync();
        var runID = await SeedRunAsync(
            TaskID: taskID,
            WorkerID: 7,
            Status: RunStatus.RUNNING);
        await using var db = CreateDbContext();

        var file = await CreateRepository(db).GetFileForWorkerRunAsync(
            RunID: runID,
            AttachmentID: attachment.ID,
            WorkerID: 8,
            CT: CancellationToken.None);

        Assert.Null(file);
    }

    [Fact]
    public async Task GetFileForWorkerRunAsync_RunNoLongerRunning_ReturnsNull()
    {
        var (taskID, attachment) = await SeedAttachmentAsync();
        var runID = await SeedRunAsync(
            TaskID: taskID,
            WorkerID: 7,
            Status: RunStatus.SUCCEEDED);
        await using var db = CreateDbContext();

        var file = await CreateRepository(db).GetFileForWorkerRunAsync(
            RunID: runID,
            AttachmentID: attachment.ID,
            WorkerID: 7,
            CT: CancellationToken.None);

        Assert.Null(file);
    }

    [Fact]
    public async Task GetFileForWorkerRunAsync_Capture_ReturnsNull()
    {
        var (taskID, attachment) = await SeedAttachmentAsync();
        await using (var setup = CreateDbContext())
        {
            var stored = await setup.TaskAttachments.SingleAsync(a => a.ID == attachment.ID);
            stored.IsCapture = true;
            await setup.SaveChangesAsync();
        }
        var runID = await SeedRunAsync(
            TaskID: taskID,
            WorkerID: 7,
            Status: RunStatus.RUNNING);
        await using var db = CreateDbContext();

        var file = await CreateRepository(db).GetFileForWorkerRunAsync(
            RunID: runID,
            AttachmentID: attachment.ID,
            WorkerID: 7,
            CT: CancellationToken.None);

        Assert.Null(file);
    }

    // ------------------------------------------------------------------ AddCapturesAsync

    [Fact]
    public async Task AddCapturesAsync_StoresImagesAsCapturesWithTheirContentAndLogsTheEvent()
    {
        var taskID = await SeedTaskAsync();
        await using var db = CreateDbContext();

        await CreateRepository(db).AddCapturesAsync(
            TaskID: taskID,
            Files: [TestFiles.CreatePng("antes.png"), TestFiles.CreatePng("despues.png")],
            Actor: "qa",
            CT: CancellationToken.None);

        await using var check = CreateDbContext();
        var captures = await check.TaskAttachments.Include(a => a.Content).OrderBy(a => a.ID).ToListAsync();
        Assert.Equal(["antes.png", "despues.png"], captures.Select(c => c.FileName));
        Assert.All(captures, c =>
        {
            Assert.True(c.IsCapture);
            Assert.Equal(AttachmentKind.IMAGE, c.Kind);
            Assert.Equal(TestFiles.PngBytes, c.Content!.Data);
        });
        Assert.Contains(await check.TaskEvents.ToListAsync(), e => e.Message == "qa agregó 2 captura(s)");
    }

    [Fact]
    public async Task AddCapturesAsync_WithNonImage_ThrowsAndStoresNothing()
    {
        var taskID = await SeedTaskAsync();
        await using var db = CreateDbContext();
        var pdf = TestFiles.Create("spec.pdf", System.Text.Encoding.ASCII.GetBytes("%PDF-1.7\n"));

        var ex = await Assert.ThrowsAsync<ValidationFailedException>(() => CreateRepository(db).AddCapturesAsync(
            TaskID: taskID,
            Files: [TestFiles.CreatePng("ok.png"), pdf],
            Actor: "qa",
            CT: CancellationToken.None));

        Assert.Contains("«spec.pdf» no es una imagen", ex.Message);
        await using var check = CreateDbContext();
        Assert.Equal(0, await check.TaskAttachments.CountAsync());
    }

    [Fact]
    public async Task AddCapturesAsync_WithoutFiles_Throws()
    {
        var taskID = await SeedTaskAsync();
        await using var db = CreateDbContext();

        var ex = await Assert.ThrowsAsync<ValidationFailedException>(() => CreateRepository(db).AddCapturesAsync(
            TaskID: taskID,
            Files: [],
            Actor: "qa",
            CT: CancellationToken.None));

        Assert.Contains("al menos una imagen", ex.Message);
    }

    [Fact]
    public async Task AddCapturesAsync_CountsTheExistingAttachmentsForTheLimit()
    {
        var (taskID, _) = await SeedAttachmentAsync();
        await using var db = CreateDbContext();

        var ex = await Assert.ThrowsAsync<ValidationFailedException>(() => CreateRepository(db, MaxFilesPerTask: 1).AddCapturesAsync(
            TaskID: taskID,
            Files: [TestFiles.CreatePng("extra.png")],
            Actor: "qa",
            CT: CancellationToken.None));

        Assert.Contains("Máximo 1", ex.Message);
    }

    [Fact]
    public async Task AddCapturesAsync_WhenTheDatabaseRejectsIt_StoresNothing()
    {
        var taskID = await SeedTaskAsync();
        await using (var db = new FailingDbContext(TestDatabase.CreateOptions(_DatabaseName), FailOnSave: 1))
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => CreateRepository(db).AddCapturesAsync(
                TaskID: taskID,
                Files: [TestFiles.CreatePng()],
                Actor: "qa",
                CT: CancellationToken.None));
        }

        await using var check = CreateDbContext();
        Assert.Equal(0, await check.TaskAttachments.CountAsync());
    }
}
