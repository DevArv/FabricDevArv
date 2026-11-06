using System.Text;
using DevArv.Web.Enums.Tasks;
using DevArv.Web.Helpers;

namespace DevArv.Tests.Web.Helpers;

public class FileSignatureTests
{
    private static readonly byte[] _Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13];
    private static readonly byte[] _Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n%âãÏÓ");

    [Fact]
    public void Png_IsAccepted() => Assert.Equal(AttachmentKind.IMAGE, FileSignature.Detect(_Png, "captura.PNG")?.Kind);

    [Fact]
    public void Pdf_IsAccepted() => Assert.Equal("application/pdf", FileSignature.Detect(_Pdf, "spec.pdf")?.ContentType);

    [Theory]
    [InlineData("<!DOCTYPE html><html><body>hola</body></html>")]
    [InlineData("\n  <html lang=\"es\"><head></head></html>")]
    [InlineData("<!-- generado por Claude --><!doctype html><html></html>")]
    public void Html_IsAccepted(string Content) =>
        Assert.Equal(AttachmentKind.HTML, FileSignature.Detect(Encoding.UTF8.GetBytes(Content), "artifact.html")?.Kind);

    [Fact]
    public void Html_WithBom_IsAccepted()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("<!doctype html><p>á</p>")).ToArray();
        Assert.NotNull(FileSignature.Detect(bytes, "a.htm"));
    }

    [Theory]
    [InlineData("captura.pdf")]   // PNG con extensión de PDF
    [InlineData("captura.html")]
    [InlineData("captura.exe")]
    public void ExtensionMustMatchContent(string Name) => Assert.Null(FileSignature.Detect(_Png, Name));

    [Fact]
    public void Executable_RenamedAsPng_IsRejected() =>
        Assert.Null(FileSignature.Detect(Encoding.ASCII.GetBytes("MZ\x90\0\x03\0\0\0"), "foto.png"));

    [Fact]
    public void PlainText_RenamedAsHtml_IsRejected() =>
        Assert.Null(FileSignature.Detect(Encoding.UTF8.GetBytes("echo hola"), "script.html"));

    [Fact]
    public void Svg_IsRejected() =>
        Assert.Null(FileSignature.Detect(Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>"), "x.svg"));
}
