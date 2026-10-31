using System.Text;
using DevArv.Contracts;
using DevArv.Web.Domain;
using DevArv.Web.Pages.Projects;
using DevArv.Web.Services;
using Microsoft.Extensions.Configuration;

namespace DevArv.Tests;

public class SlugTests
{
    [Theory]
    [InlineData("Requerimientos de mejora para Fabric", "requerimientos-de-mejora-para-fabric")]
    [InlineData("  Añadir validación  ¡ya!  ", "anadir-validacion-ya")]
    [InlineData("###", "tarea")]
    public void Create_ProducesGitSafeSlug(string input, string expected) =>
        Assert.Equal(expected, Slug.Create(input));

    [Fact]
    public void Create_RespectsMaxLength() =>
        Assert.True(Slug.Create(new string('a', 200), 50).Length <= 50);
}

public class FileSignatureTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13];
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n%âãÏÓ");

    [Fact]
    public void Png_IsAccepted() => Assert.Equal(AttachmentKind.Image, FileSignature.Detect(Png, "captura.PNG")?.Kind);

    [Fact]
    public void Pdf_IsAccepted() => Assert.Equal("application/pdf", FileSignature.Detect(Pdf, "spec.pdf")?.ContentType);

    [Theory]
    [InlineData("<!DOCTYPE html><html><body>hola</body></html>")]
    [InlineData("\n  <html lang=\"es\"><head></head></html>")]
    [InlineData("<!-- generado por Claude --><!doctype html><html></html>")]
    public void Html_IsAccepted(string content) =>
        Assert.Equal(AttachmentKind.Html, FileSignature.Detect(Encoding.UTF8.GetBytes(content), "artifact.html")?.Kind);

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
    public void ExtensionMustMatchContent(string name) => Assert.Null(FileSignature.Detect(Png, name));

    [Fact]
    public void Executable_RenamedAsPng_IsRejected() =>
        Assert.Null(FileSignature.Detect(Encoding.ASCII.GetBytes("MZ\x90\0\x03\0\0\0"), "foto.png"));

    [Fact]
    public void PlainText_RenamedAsHtml_IsRejected() =>
        Assert.Null(FileSignature.Detect(Encoding.UTF8.GetBytes("echo hola"), "script.html"));

    [Fact]
    public void Svg_IsRejected() =>
        Assert.Null(FileSignature.Detect(Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>"), "x.svg"));

    [Theory]
    [InlineData("C:\\fakepath\\mi:archivo?.png", "mi_archivo_.png")]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("...", "archivo")]
    public void SanitizeFileName_RemovesPathsAndInvalidChars(string input, string expected) =>
        Assert.Equal(expected, AttachmentStorage.SanitizeFileName(input));
}

public class DecisionParserTests
{
    [Theory]
    [InlineData("Todo bien.\nVEREDICTO: APROBADO", RunDecision.Approved)]
    [InlineData("Hallazgos:\n- bug en X\n\nVEREDICTO: RECHAZADO", RunDecision.Rejected)]
    [InlineData("**Veredicto:** **aprobado**", RunDecision.Approved)]
    [InlineData("Veredicto: **APROBADO**", RunDecision.Approved)]
    [InlineData("Si fuera grave diría VEREDICTO: RECHAZADO, pero no.\nVEREDICTO: APROBADO", RunDecision.Approved)]
    [InlineData("VERDICT: REJECTED", RunDecision.Rejected)]
    [InlineData("sin veredicto", RunDecision.None)]
    [InlineData(null, RunDecision.None)]
    public void Parse_UsesLastVerdict(string? text, RunDecision expected) =>
        Assert.Equal(expected, DecisionParser.Parse(text));
}

public class RepositoryParsingTests
{
    [Theory]
    [InlineData("mi-org/mi-repo", "mi-org", "mi-repo")]
    [InlineData("https://github.com/mi-org/mi-repo", "mi-org", "mi-repo")]
    [InlineData("https://github.com/mi-org/mi-repo.git", "mi-org", "mi-repo")]
    [InlineData("git@github.com:mi-org/mi.repo.git", "mi-org", "mi.repo")]
    public void Parses(string input, string owner, string repo)
    {
        Assert.True(EditModel.TryParseRepository(input, out var o, out var r));
        Assert.Equal(owner, o);
        Assert.Equal(repo, r);
    }

    [Theory]
    [InlineData("https://gitlab.com/a/b")]
    [InlineData("solo-un-nombre")]
    [InlineData("a/b/c")]
    public void RejectsInvalid(string input) => Assert.False(EditModel.TryParseRepository(input, out _, out _));
}

public class PromptBuilderTests
{
    private static PromptBuilder Builder() =>
        new(new Clock(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["App:TimeZone"] = "UTC",
        }).Build()));

    [Fact]
    public void Build_FillsVariablesAttachmentsAndFeedback()
    {
        var project = new Project { Name = "Fabric", RepoOwner = "acme", RepoName = "fabric", AgentInstructions = "Usa C#." };
        var task = new TaskItem
        {
            Id = 222, Title = "Más adjuntos", Description = "Subir el límite a 15.", Project = project,
            BaseBranch = "master", WorkBranch = "devarv/222-mas-adjuntos",
            Attachments = [new TaskAttachment { Id = 7, FileName = "spec.pdf", Kind = AttachmentKind.Pdf, ContentType = "application/pdf" }],
            Comments =
            [
                new TaskComment { Kind = CommentKind.Comment, Author = "ana", Body = "no se envía" },
                new TaskComment { Kind = CommentKind.ReviewerNote, Author = "Revisor", Body = "Falta validar en backend.", CreatedAt = DateTimeOffset.UtcNow },
            ],
        };
        var stage = new Stage { Kind = StageKind.Agent, PushChanges = true, AppendSystemPrompt = "Responde en español." };

        var prompt = Builder().Build(task, stage);

        Assert.Contains("Tarea #222: Más adjuntos", prompt);
        Assert.Contains("acme/fabric", prompt);
        Assert.Contains("`devarv/222-mas-adjuntos`", prompt);
        Assert.Contains("7-spec.pdf", prompt);
        Assert.Contains(PromptTokens.AttachmentsDir, prompt);
        Assert.Contains("Falta validar en backend.", prompt);
        Assert.DoesNotContain("no se envía", prompt);
        Assert.DoesNotContain("{{", prompt.Replace(PromptTokens.AttachmentsDir, ""));
        Assert.Equal("Usa C#.\n\nResponde en español.", PromptBuilder.BuildSystemAppend(task, stage));
    }
}

public class MarkdownRendererTests
{
    [Fact]
    public void EscapesHtmlAndUnsafeLinks()
    {
        var html = MarkdownRenderer.Render("<script>alert(1)</script> [x](javascript:alert(1)) [ok](https://github.com)").ToString()!;
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("javascript:", html);
        Assert.Contains("https://github.com", html);
    }
}
