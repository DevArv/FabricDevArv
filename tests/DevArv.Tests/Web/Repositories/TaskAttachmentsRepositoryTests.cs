using DevArv.Web.DatabaseConnection;
using DevArv.Web.DataModels;
using DevArv.Web.Enums.Tasks;
using DevArv.Web.Enums.Workflow;
using DevArv.Web.Models;
using DevArv.Web.Repositories;
using DevArv.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DevArv.Tests.Web.Repositories;

public sealed class TaskAttachmentsRepositoryTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    private readonly string _DatabaseName = Guid.NewGuid().ToString("N");

    [Fact]
    public async Task AddAsync_SavesFileContentInDatabase()
    {
        var taskId = await SeedTaskAsync();

        await using (var db = CreateDbContext())
        {
            await CreateRepository(db).AddAsync(
                taskId: taskId,
                files: [FormFile("captura.png", Png)],
                actor: "tester",
                ct: CancellationToken.None);
        }

        await using var check = CreateDbContext();
        var attachment = await check.TaskAttachments.Include(a => a.Content).SingleAsync();
        Assert.Equal("captura.png", attachment.FileName);
        Assert.Equal("image/png", attachment.ContentType);
        Assert.Equal(AttachmentKind.Image, attachment.Kind);
        Assert.Equal(Png.Length, attachment.Size);
        Assert.NotNull(attachment.Content);
        Assert.Equal(Png, attachment.Content.Data);
    }

    [Fact]
    public async Task AddAsync_InvalidFile_ThrowsAndSavesNothing()
    {
        var taskId = await SeedTaskAsync();

        await using var db = CreateDbContext();
        var ex = await Assert.ThrowsAsync<ValidationFailedException>(() => CreateRepository(db).AddAsync(
            taskId: taskId,
            files: [FormFile("captura.png", "no es una imagen"u8.ToArray())],
            actor: "tester",
            ct: CancellationToken.None));

        Assert.NotEmpty(ex.Message);
        await using var check = CreateDbContext();
        Assert.Equal(0, await check.TaskAttachments.CountAsync());
    }

    [Fact]
    public async Task GetFileAsync_ReturnsContentOnlyForTheOwnerTask()
    {
        var taskId = await SeedTaskAsync();
        await using (var db = CreateDbContext())
        {
            await CreateRepository(db).AddAsync(taskId, [FormFile("captura.png", Png)], "tester", CancellationToken.None);
        }

        await using var read = CreateDbContext();
        var repository = CreateRepository(read);
        var attachmentId = await read.TaskAttachments.Select(a => a.Id).SingleAsync();

        var file = await repository.GetFileAsync(taskId, attachmentId, CancellationToken.None);
        var otherTask = await repository.GetFileAsync(taskId + 1, attachmentId, CancellationToken.None);

        Assert.NotNull(file);
        Assert.Equal(Png, file.Content!.Data);
        Assert.Null(otherTask);
    }

    [Fact]
    public async Task DeleteAsync_RemovesAttachment()
    {
        var taskId = await SeedTaskAsync();
        await using (var db = CreateDbContext())
        {
            await CreateRepository(db).AddAsync(taskId, [FormFile("captura.png", Png)], "tester", CancellationToken.None);
        }

        await using (var db = CreateDbContext())
        {
            var attachmentId = await db.TaskAttachments.Select(a => a.Id).SingleAsync();
            await CreateRepository(db).DeleteAsync(taskId, attachmentId, "tester", CancellationToken.None);
        }

        await using var check = CreateDbContext();
        Assert.Equal(0, await check.TaskAttachments.CountAsync());
    }

    private static FormFile FormFile(string fileName, byte[] data) =>
        new(new MemoryStream(data), 0, data.Length, "files", fileName);

    private AppDbContext CreateDbContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(_DatabaseName)
        .Options);

    private static TaskAttachmentsRepository CreateRepository(AppDbContext db) =>
        new(db, new AttachmentStorage(Options.Create(new AttachmentOptions())));

    private async Task<int> SeedTaskAsync()
    {
        await using var db = CreateDbContext();
        db.Projects.Add(new Project { Id = 1, Name = "Proyecto", RepoOwner = "o", RepoName = "r" });
        db.Stages.Add(new Stage { Id = 1, Name = "Backlog", Kind = StageKind.Manual });
        var task = new TaskItem { Title = "Tarea", ProjectId = 1, StageId = 1, CreatedByName = "tester" };
        db.Tasks.Add(task);
        await db.SaveChangesAsync();
        return task.Id;
    }
}
