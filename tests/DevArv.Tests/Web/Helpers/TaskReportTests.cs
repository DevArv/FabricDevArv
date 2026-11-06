using DevArv.Web.DataModels;
using DevArv.Web.Enums.Runs;
using DevArv.Web.Enums.Workflow;
using DevArv.Web.Helpers;

namespace DevArv.Tests.Web.Helpers;

public class TaskReportTests
{
    private const int DEVELOPER_STAGE_ID = 4;
    private const int REVIEWER_STAGE_ID = 5;
    private const int PULL_REQUEST_STAGE_ID = 6;
    private const int APPROVAL_STAGE_ID = 7;

    private static readonly Stage[] Stages =
    [
        new() { ID = DEVELOPER_STAGE_ID, Name = "Desarrollador", Kind = StageKind.AGENT, PushChanges = true },
        new() { ID = REVIEWER_STAGE_ID, Name = "Revisor de Código", Kind = StageKind.AGENT, RequiresDecision = true },
        new() { ID = PULL_REQUEST_STAGE_ID, Name = "Creador de PR", Kind = StageKind.SYSTEM, SystemAction = SystemAction.CREATE_PULL_REQUEST },
        new() { ID = APPROVAL_STAGE_ID, Name = "Aprobación Producto", Kind = StageKind.HUMAN_APPROVAL },
    ];

    private const string DeveloperSummary = """
        Implementé las pestañas.

        ## Pruebas unitarias
        `dotnet test`: 120 pruebas, 120 aprobadas, 0 fallidas.

        ### Pruebas nuevas
        - TaskReportTests

        ## Pruebas QA
        1. Abrir una tarea en «Aprobación Producto».
        2. Ver las pestañas.

        ## Riesgos
        Ninguno.
        """;

    // ------------------------------------------------------------------ ExtractSection

    [Fact]
    public void ExtractSection_ReturnsContentUntilNextHeadingOfSameLevel()
    {
        var unit = TaskReport.ExtractSection(
            Markdown: DeveloperSummary,
            Headings: TaskReport.UNIT_TESTS_HEADINGS);

        Assert.Equal("`dotnet test`: 120 pruebas, 120 aprobadas, 0 fallidas.\n\n### Pruebas nuevas\n- TaskReportTests", unit);
    }

    [Fact]
    public void ExtractSection_LastSectionBeforeAnotherHeading()
    {
        var qa = TaskReport.ExtractSection(
            Markdown: DeveloperSummary,
            Headings: TaskReport.QA_TESTS_HEADINGS);

        Assert.Equal("1. Abrir una tarea en «Aprobación Producto».\n2. Ver las pestañas.", qa);
    }

    [Theory]
    [InlineData("### pruebas UNITARIAS:\nok")]
    [InlineData("# **Pruebas unitarias**\nok")]
    [InlineData("## Pruebas unitarias ##\nok")]
    [InlineData("## Pruebas Unitárias\r\nok")]
    public void ExtractSection_IgnoresCaseAccentsAndDecorations(string Markdown) =>
        Assert.Equal("ok", TaskReport.ExtractSection(
            Markdown: Markdown,
            Headings: TaskReport.UNIT_TESTS_HEADINGS));

    [Fact]
    public void ExtractSection_AcceptsQaAlias() =>
        Assert.Equal("pasos", TaskReport.ExtractSection(
            Markdown: "## Pruebas de QA\npasos",
            Headings: TaskReport.QA_TESTS_HEADINGS));

    [Fact]
    public void ExtractSection_IgnoresHeadingsInsideCodeBlocks()
    {
        const string MARKDOWN = "## Pruebas unitarias\n```\n## Pruebas QA\n```\nfin\n## Otra\nx";

        Assert.Equal("```\n## Pruebas QA\n```\nfin", TaskReport.ExtractSection(
            Markdown: MARKDOWN,
            Headings: TaskReport.UNIT_TESTS_HEADINGS));
        Assert.Null(TaskReport.ExtractSection(
            Markdown: MARKDOWN,
            Headings: TaskReport.QA_TESTS_HEADINGS));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Sin secciones.")]
    [InlineData("## Pruebas unitarias\n\n## Pruebas QA\npasos")]
    [InlineData("Pruebas unitarias: todas pasan")]
    public void ExtractSection_MissingOrEmpty_ReturnsNull(string? Markdown) =>
        Assert.Null(TaskReport.ExtractSection(
            Markdown: Markdown,
            Headings: TaskReport.UNIT_TESTS_HEADINGS));

    // ------------------------------------------------------------------ FindSection

    [Fact]
    public void FindSection_UsesMostRecentSucceededDeveloperRunThatHasTheSection()
    {
        TaskRun[] runs =
        [
            Run(1, DEVELOPER_STAGE_ID, RunStatus.SUCCEEDED, "## Pruebas unitarias\nviejo"),
            Run(2, DEVELOPER_STAGE_ID, RunStatus.SUCCEEDED, "## Pruebas unitarias\nnuevo"),
            // Arreglo de conflictos sin la sección: se salta.
            Run(3, DEVELOPER_STAGE_ID, RunStatus.SUCCEEDED, "Resolví los conflictos."),
            // Fallida o de otra columna: no cuenta.
            Run(4, DEVELOPER_STAGE_ID, RunStatus.FAILED, "## Pruebas unitarias\nfallida"),
            Run(5, REVIEWER_STAGE_ID, RunStatus.SUCCEEDED, "## Pruebas unitarias\nrevisor"),
        ];

        var section = TaskReport.FindSection(
            Runs: runs,
            Stages: Stages,
            Headings: TaskReport.UNIT_TESTS_HEADINGS);

        Assert.NotNull(section);
        Assert.Equal("nuevo", section.Content);
        Assert.Equal(2, section.RunID);
        Assert.Equal("Desarrollador", section.StageName);
    }

    [Fact]
    public void FindSection_WithoutReport_ReturnsNull() =>
        Assert.Null(TaskReport.FindSection(
            Runs: [Run(1, DEVELOPER_STAGE_ID, RunStatus.SUCCEEDED, "Listo.")],
            Stages: Stages,
            Headings: TaskReport.QA_TESTS_HEADINGS));

    // ------------------------------------------------------------------ ShowTabs

    [Fact]
    public void ShowTabs_BeforePullRequestCreator_IsFalse()
    {
        var task = new TaskItem { StageID = REVIEWER_STAGE_ID, Runs = [Run(1, DEVELOPER_STAGE_ID, RunStatus.SUCCEEDED, "x")] };

        Assert.False(TaskReport.ShowTabs(
            Item: task,
            Stages: Stages));
    }

    [Fact]
    public void ShowTabs_InPullRequestCreator_IsTrue() =>
        Assert.True(TaskReport.ShowTabs(
            Item: new TaskItem { StageID = PULL_REQUEST_STAGE_ID },
            Stages: Stages));

    [Fact]
    public void ShowTabs_AfterPullRequestCreatorRun_IsTrue()
    {
        var task = new TaskItem { StageID = DEVELOPER_STAGE_ID, Runs = [Run(1, PULL_REQUEST_STAGE_ID, RunStatus.SUCCEEDED, null)] };

        Assert.True(TaskReport.ShowTabs(
            Item: task,
            Stages: Stages));
    }

    [Fact]
    public void ShowTabs_WithPullRequest_IsTrue() =>
        Assert.True(TaskReport.ShowTabs(
            Item: new TaskItem { StageID = APPROVAL_STAGE_ID, PullRequestUrl = "https://github.com/o/r/pull/1" },
            Stages: Stages));

    // ------------------------------------------------------------------ CountLines

    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("una", 1)]
    [InlineData("una\n", 1)]
    [InlineData("una\ndos\ntres", 3)]
    [InlineData("una\ndos\ntres\n", 3)]
    public void CountLines_IgnoresTrailingNewLine(string? Text, int Expected) =>
        Assert.Equal(Expected, TaskReport.CountLines(Text));

    private static TaskRun Run(long ID, int StageID, RunStatus Status, string? Summary) => new()
    {
        ID = ID,
        StageID = StageID,
        StageName = Stages.Single(s => s.ID == StageID).Name,
        RunnerKind = Stages.Single(s => s.ID == StageID).Kind,
        Status = Status,
        Summary = Summary,
    };
}
