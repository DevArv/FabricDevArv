using DevArv.Web.Models;
using DevArv.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace DevArv.Tests.Web.Services;

public class AttachmentStorageTests
{
    private static readonly byte[] Pdf = "%PDF-1.7 contenido"u8.ToArray();

    [Theory]
    [InlineData("C:\\fakepath\\mi:archivo?.png", "mi_archivo_.png")]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("...", "archivo")]
    public void SanitizeFileName_RemovesPathsAndInvalidChars(string input, string expected) =>
        Assert.Equal(expected, AttachmentStorage.SanitizeFileName(input));

    [Fact]
    public async Task CreateAsync_KeepsWholeFileInMemoryForTheDatabase()
    {
        var storage = new AttachmentStorage(Options.Create(new AttachmentOptions()));

        var attachments = await storage.CreateAsync(
            taskId: 5,
            files: [FormFile("spec.pdf", Pdf)],
            existingCount: 0,
            ct: CancellationToken.None);

        var attachment = Assert.Single(attachments);
        Assert.Equal(5, attachment.TaskId);
        Assert.Equal("spec.pdf", attachment.FileName);
        Assert.Equal("application/pdf", attachment.ContentType);
        Assert.Equal(Pdf.Length, attachment.Size);
        Assert.Equal(Pdf, attachment.Content!.Data);
    }

    [Fact]
    public async Task CreateAsync_TooManyFiles_Throws()
    {
        var storage = new AttachmentStorage(Options.Create(new AttachmentOptions { MaxFilesPerTask = 1 }));

        await Assert.ThrowsAsync<AttachmentValidationException>(() => storage.CreateAsync(
            taskId: 5,
            files: [FormFile("spec.pdf", Pdf)],
            existingCount: 1,
            ct: CancellationToken.None));
    }

    private static FormFile FormFile(string fileName, byte[] data) =>
        new(new MemoryStream(data), 0, data.Length, "files", fileName);
}
