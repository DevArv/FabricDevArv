using Microsoft.AspNetCore.Http;

namespace DevArv.Tests.TestSupport;

public static class TestFiles
{
    /// <summary>Cabecera PNG válida seguida de unos bytes de relleno.</summary>
    public static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];

    public static IFormFile Create(string FileName, byte[] Content) =>
        new FormFile(new MemoryStream(Content), 0, Content.Length, "files", FileName);

    public static IFormFile CreatePng(string FileName = "captura.png") => Create(FileName, PngBytes);
}
