using DevArv.Tests.TestSupport;
using DevArv.Web.Models;
using DevArv.Web.Services;
using Microsoft.Extensions.Options;

namespace DevArv.Tests.Web.Services;

public class AttachmentStorageTests
{
    private static readonly byte[] _Pdf = "%PDF-1.7 contenido"u8.ToArray();

    [Theory]
    [InlineData("C:\\fakepath\\mi:archivo?.png", "mi_archivo_.png")]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("...", "archivo")]
    public void SanitizeFileName_RemovesPathsAndInvalidChars(string Input, string Expected) =>
        Assert.Equal(Expected, AttachmentStorage.SanitizeFileName(Input));

    [Fact]
    public async Task CreateAsync_KeepsWholeFileInMemoryForTheDatabase()
    {
        var storage = new AttachmentStorage(Options.Create(new AttachmentOptions()));

        var attachments = await storage.CreateAsync(
            TaskID: 5,
            Files: [TestFiles.Create(FileName: "spec.pdf", Content: _Pdf)],
            ExistingCount: 0,
            CT: CancellationToken.None);

        var attachment = Assert.Single(attachments);
        Assert.Equal(5, attachment.TaskID);
        Assert.Equal("spec.pdf", attachment.FileName);
        Assert.Equal("application/pdf", attachment.ContentType);
        Assert.Equal(_Pdf.Length, attachment.Size);
        Assert.Equal(_Pdf, attachment.Content!.Data);
    }

    [Fact]
    public async Task CreateAsync_TooManyFiles_Throws()
    {
        var storage = new AttachmentStorage(Options.Create(new AttachmentOptions { MaxFilesPerTask = 1 }));

        await Assert.ThrowsAsync<AttachmentValidationException>(() => storage.CreateAsync(
            TaskID: 5,
            Files: [TestFiles.Create(FileName: "spec.pdf", Content: _Pdf)],
            ExistingCount: 1,
            CT: CancellationToken.None));
    }
}
