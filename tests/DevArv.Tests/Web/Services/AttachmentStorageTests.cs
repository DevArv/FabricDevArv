using DevArv.Web.Services;

namespace DevArv.Tests.Web.Services;

public class AttachmentStorageTests
{
    [Theory]
    [InlineData("C:\\fakepath\\mi:archivo?.png", "mi_archivo_.png")]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("...", "archivo")]
    public void SanitizeFileName_RemovesPathsAndInvalidChars(string input, string expected) =>
        Assert.Equal(expected, AttachmentStorage.SanitizeFileName(input));
}
