using System.Text.RegularExpressions;
using DevArv.Tests.TestSupport;
using DevArv.Web.Enums.Runs;
using DevArv.Web.Enums.Tasks;
using DevArv.Web.Enums.Workflow;
using DevArv.Web.Helpers;

namespace DevArv.Tests.Web.Pages;

/// <summary>
/// Las vistas, el JavaScript y el CSS se enlazan por texto con los enums y con las clases de <see cref="Labels"/>.
/// Nada de eso lo comprueba el compilador: estas pruebas son la red de seguridad.
/// </summary>
public class ViewContractsTests
{
    private static string Read(params string[] RelativePath) =>
        File.ReadAllText(Path.Combine([RepositoryPaths.WebProject, .. RelativePath]));

    private static HashSet<string> CssClasses() =>
        Regex.Matches(Read("wwwroot", "css", "site.css"), @"\.([a-z][a-z0-9_-]*)").Select(m => m.Groups[1].Value).ToHashSet();

    [Fact]
    public void StageEditor_ConditionalSections_PointToRealStageKinds()
    {
        var view = Read("Pages", "Workflow", "Edit.cshtml");

        var values = Regex.Matches(view, "data-kind-show=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToList();

        Assert.NotEmpty(values);
        // El JS compara este valor con el del <select>, que se genera con el nombre del miembro del enum.
        // Un texto fijo («Agent») dejaría esas secciones ocultas para siempre.
        var allowed = Enum.GetNames<StageKind>().Select(name => $"@StageKind.{name}").ToHashSet();
        Assert.All(values, value => Assert.Contains(value, allowed));
        Assert.Contains("@StageKind.AGENT", values);
        Assert.Contains("@StageKind.SYSTEM", values);
    }

    [Fact]
    public void StageEditor_JsComparesTheSectionWithTheSelectValue_AsWeExpect() =>
        Assert.Contains("f.dataset.kindShow !== select.value", Read("wwwroot", "js", "site.js"));

    [Fact]
    public void StageKindCss_EveryColumnTypeGetsAClass_AndTheStyledOnesExistInTheStylesheet()
    {
        var css = CssClasses();

        // Orden de los miembros del enum: MANUAL, HUMAN_APPROVAL, AGENT, SYSTEM, TERMINAL.
        Assert.Equal(
            ["kind-manual", "kind-humanapproval", "kind-agent", "kind-system", "kind-terminal"],
            Enum.GetValues<StageKind>().Select(Labels.KindCss));
        foreach (var styled in new[] { "kind-agent", "kind-system", "kind-humanapproval", "kind-terminal" })
            Assert.Contains(styled, css);
    }

    [Fact]
    public void CommentKindCss_StyledKindsExistInTheStylesheet()
    {
        var css = CssClasses();

        Assert.Equal("kind-feedback", Labels.KindCss(CommentKind.FEEDBACK));
        Assert.Equal("kind-reviewernote", Labels.KindCss(CommentKind.REVIEWER_NOTE));
        Assert.Equal("kind-comment", Labels.KindCss(CommentKind.COMMENT));
        Assert.Contains("kind-feedback", css);
        Assert.Contains("kind-reviewernote", css);
    }

    [Fact]
    public void RunStatusCss_StyledStatusesExistInTheStylesheet()
    {
        var css = CssClasses();

        Assert.Equal(
            ["rs-queued", "rs-running", "rs-succeeded", "rs-failed", "rs-cancelled"],
            Enum.GetValues<RunStatus>().Select(Labels.StatusCss));
        foreach (var styled in new[] { "rs-running", "rs-succeeded", "rs-failed", "rs-cancelled" })
            Assert.Contains(styled, css);
    }

    [Fact]
    public void WorkStatusCss_StyledStatusesExistInTheStylesheet()
    {
        var css = CssClasses();

        foreach (var status in Enum.GetValues<WorkStatus>())
            Assert.StartsWith("st-", Labels.StatusCss(status));
        foreach (var styled in new[] { "st-queued", "st-running", "st-waiting", "st-failed", "st-done", "st-idle" })
            Assert.Contains(styled, css);
    }

    [Fact]
    public void Views_DoNotBuildCssClassesFromEnumNames() =>
        Assert.All(Directory.EnumerateFiles(Path.Combine(RepositoryPaths.WebProject, "Pages"), "*.cshtml", SearchOption.AllDirectories), file =>
            Assert.DoesNotMatch(@"(Kind|Status|Priority|Complexity|Decision)\.ToString\(\)\.ToLowerInvariant\(\)", File.ReadAllText(file)));
}
