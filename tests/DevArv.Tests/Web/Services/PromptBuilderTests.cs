using DevArv.Contracts.Models;
using DevArv.Web.Constants;
using DevArv.Web.DataModels;
using DevArv.Web.Enums.Tasks;
using DevArv.Web.Enums.Workflow;
using DevArv.Web.Helpers;
using DevArv.Web.Services;
using DevArv.Web.Models;
using Microsoft.Extensions.Options;

namespace DevArv.Tests.Web.Services;

public class PromptBuilderTests
{
    private static PromptBuilder Builder() =>
        new(new Clock(Options.Create(new AppOptions { TimeZone = "UTC" })));

    [Fact]
    public void Build_FillsVariablesAttachmentsAndFeedback()
    {
        var project = new Project { Name = "Fabric", RepoOwner = "acme", RepoName = "fabric", AgentInstructions = "Usa C#." };
        var task = new TaskItem
        {
            ID = 222, Title = "Más adjuntos", Description = "Subir el límite a 15.", Project = project,
            BaseBranch = "master", WorkBranch = "devarv/222-mas-adjuntos",
            Attachments = [new TaskAttachment { ID = 7, FileName = "spec.pdf", Kind = AttachmentKind.PDF, ContentType = "application/pdf" }],
            Comments =
            [
                new TaskComment { Kind = CommentKind.COMMENT, Author = "ana", Body = "no se envía" },
                new TaskComment { Kind = CommentKind.REVIEWER_NOTE, Author = "Revisor", Body = "Falta validar en backend.", CreatedAt = DateTimeOffset.UtcNow },
            ],
        };
        var stage = new Stage { Kind = StageKind.AGENT, PushChanges = true, AppendSystemPrompt = "Responde en español." };

        var prompt = Builder().Build(task, stage);

        Assert.Contains("Tarea #222: Más adjuntos", prompt);
        Assert.Contains("acme/fabric", prompt);
        Assert.Contains("`devarv/222-mas-adjuntos`", prompt);
        Assert.Contains("7-spec.pdf", prompt);
        Assert.Contains(PromptTokens.ATTACHMENTS_DIR, prompt);
        Assert.Contains("Falta validar en backend.", prompt);
        Assert.DoesNotContain("no se envía", prompt);
        Assert.DoesNotContain("{{", prompt.Replace(PromptTokens.ATTACHMENTS_DIR, ""));
        Assert.Equal("Usa C#.\n\nResponde en español.", PromptBuilder.BuildSystemAppend(task, stage));
    }

    private static TaskItem ReviewTask() => new()
    {
        ID = 4, Title = "Skill en revisor", Description = "Usar el skill revision.", BaseBranch = "main",
        WorkBranch = "fd_/4-skill", Project = new Project { Name = "Fabric", RepoOwner = "DevArv", RepoName = "FabricDevArv" },
    };

    [Fact]
    public void Build_ReviewerWithSkill_PlacesSkillBlockBeforeReviewSteps()
    {
        var stage = new Stage
        {
            Kind = StageKind.AGENT, RequiresDecision = true, PromptTemplate = DefaultPrompts.REVIEWER,
            Skill = DefaultPrompts.REVIEWER_SKILL,
        };

        var prompt = Builder().Build(ReviewTask(), stage);

        Assert.Contains("## Skill `revision`", prompt);
        Assert.Contains("`/revision`", prompt);
        Assert.True(prompt.IndexOf("## Skill `revision`", StringComparison.Ordinal)
                    < prompt.IndexOf("## Cómo revisar", StringComparison.Ordinal));
        Assert.EndsWith("VEREDICTO: RECHAZADO", prompt);
        Assert.DoesNotContain("{{", prompt);
    }

    [Fact]
    public void Build_WithoutSkill_LeavesNoSkillBlock()
    {
        var stage = new Stage { Kind = StageKind.AGENT, RequiresDecision = true, PromptTemplate = DefaultPrompts.REVIEWER };

        var prompt = Builder().Build(ReviewTask(), stage);

        Assert.DoesNotContain("## Skill", prompt);
        Assert.DoesNotContain("{{skill}}", prompt);
    }

    [Fact]
    public void Build_TemplateWithoutSkillVariable_AppendsSkillBlockAtTheEnd()
    {
        var stage = new Stage { Kind = StageKind.AGENT, PromptTemplate = "Revisa la tarea {{task.id}}.", Skill = "revision" };

        var prompt = Builder().Build(ReviewTask(), stage);

        Assert.StartsWith("Revisa la tarea 4.", prompt);
        Assert.Contains("## Skill `revision`", prompt);
    }

    [Fact]
    public void Build_ResolveConflicts_AppendsConflictInstructionsAfterTheTemplate()
    {
        var task = ReviewTask();
        task.PullRequestNumber = 31;
        var stage = new Stage { Kind = StageKind.AGENT, PushChanges = true, PromptTemplate = DefaultPrompts.DEVELOPER };

        var prompt = Builder().Build(
            Item: task,
            Stage: stage,
            ResolveConflicts: true);

        Assert.Contains("## Arreglar conflictos con `main`", prompt);
        Assert.Contains("El pull request #31", prompt);
        Assert.Contains("`origin/main`", prompt);
        Assert.Contains("git diff --name-only --diff-filter=U", prompt);
        Assert.True(prompt.IndexOf("## Cómo trabajar", StringComparison.Ordinal)
                    < prompt.IndexOf("## Arreglar conflictos", StringComparison.Ordinal));
        Assert.DoesNotContain("{{", prompt);
    }

    [Fact]
    public void Build_ResolveConflicts_UsesConflictsVariableWhenTheTemplateHasIt()
    {
        var stage = new Stage { Kind = StageKind.AGENT, PushChanges = true, PromptTemplate = "{{conflicts}}\nTarea {{task.id}}." };

        var prompt = Builder().Build(
            Item: ReviewTask(),
            Stage: stage,
            ResolveConflicts: true);

        Assert.StartsWith("## Arreglar conflictos", prompt);
        Assert.EndsWith("Tarea 4.", prompt);
    }

    [Fact]
    public void Build_WithoutResolveConflicts_LeavesNoConflictBlock()
    {
        var stage = new Stage { Kind = StageKind.AGENT, PushChanges = true, PromptTemplate = "Tarea {{task.id}}. {{conflicts}}" };

        var prompt = Builder().Build(ReviewTask(), stage);

        // El bloque de reporte de pruebas sí se agrega al final (la columna hace push).
        Assert.StartsWith("Tarea 4.\n\n## Reporte de pruebas", prompt);
        Assert.DoesNotContain("Arreglar conflictos", prompt);
    }

    [Fact]
    public void Build_Developer_AppendsTestReportSectionsAtTheEnd()
    {
        var stage = new Stage { Kind = StageKind.AGENT, PushChanges = true, PromptTemplate = DefaultPrompts.DEVELOPER };

        var prompt = Builder().Build(ReviewTask(), stage);

        Assert.Contains("## Reporte de pruebas", prompt);
        Assert.Contains($"`## {TaskReport.UNIT_TESTS_HEADING}`", prompt);
        Assert.Contains($"`## {TaskReport.QA_TESTS_HEADING}`", prompt);
        Assert.True(prompt.IndexOf("Termina con un resumen breve", StringComparison.Ordinal)
                    < prompt.IndexOf("## Reporte de pruebas", StringComparison.Ordinal));
        Assert.DoesNotContain("{{", prompt);
    }

    [Fact]
    public void Build_Developer_UsesReportVariableWhenTheTemplateHasIt()
    {
        var stage = new Stage { Kind = StageKind.AGENT, PushChanges = true, PromptTemplate = "{{report}}\nTarea {{task.id}}." };

        var prompt = Builder().Build(ReviewTask(), stage);

        Assert.StartsWith("## Reporte de pruebas", prompt);
        Assert.EndsWith("Tarea 4.", prompt);
    }

    [Fact]
    public void Build_ReviewerOrConflictFix_LeavesNoTestReportBlock()
    {
        var reviewer = new Stage { Kind = StageKind.AGENT, RequiresDecision = true, PromptTemplate = "Tarea {{task.id}}. {{report}}" };
        var developer = new Stage { Kind = StageKind.AGENT, PushChanges = true, PromptTemplate = DefaultPrompts.DEVELOPER };

        Assert.Equal("Tarea 4.", Builder().Build(ReviewTask(), reviewer));
        Assert.DoesNotContain("## Reporte de pruebas", Builder().Build(
            Item: ReviewTask(),
            Stage: developer,
            ResolveConflicts: true));
    }

    [Fact]
    public void Build_ExcludesCapturesFromAttachments()
    {
        var task = ReviewTask();
        task.Attachments =
        [
            new TaskAttachment { ID = 1, FileName = "spec.png", Kind = AttachmentKind.IMAGE, ContentType = "image/png" },
            new TaskAttachment { ID = 2, FileName = "resultado.png", Kind = AttachmentKind.IMAGE, ContentType = "image/png", IsCapture = true },
        ];
        var stage = new Stage { Kind = StageKind.AGENT, PushChanges = true, PromptTemplate = DefaultPrompts.DEVELOPER };

        var prompt = Builder().Build(task, stage);

        Assert.Contains("1-spec.png", prompt);
        Assert.DoesNotContain("resultado.png", prompt);

        task.Attachments = [task.Attachments[1]];
        Assert.DoesNotContain("## Archivos adjuntos", Builder().Build(task, stage));
    }

    [Fact]
    public void BuildAllowedTools_AddsSkillPermissionOnce()
    {
        var stage = new Stage { AllowedTools = "Read\r\nGrep\n\n Bash(git diff:*) ", Skill = "revision" };

        Assert.Equal(new[] { "Read", "Grep", "Bash(git diff:*)", "Skill(revision)" }, PromptBuilder.BuildAllowedTools(stage));

        stage.AllowedTools = "Read\nSkill(revision)";
        Assert.Equal(new[] { "Read", "Skill(revision)" }, PromptBuilder.BuildAllowedTools(stage));

        stage.Skill = null;
        stage.AllowedTools = "Read";
        Assert.Equal(new[] { "Read" }, PromptBuilder.BuildAllowedTools(stage));
    }

    [Fact]
    public void Build_PlaceholdersTypedInsideTaskData_AreNotExpandedAgain()
    {
        var task = ReviewTask();
        task.Description = "Mostrar {{feedback}} y {{project.name}} tal cual.";
        task.Comments = [new TaskComment { Kind = CommentKind.FEEDBACK, Author = "ana", Body = "SECRETO-DE-FEEDBACK", CreatedAt = DateTimeOffset.UtcNow }];
        var stage = new Stage
        {
            Kind = StageKind.AGENT,
            PromptTemplate = "Descripción: {{task.description}}\nProyecto: {{project.name}}\n{{feedback}}",
        };

        var prompt = Builder().Build(task, stage);

        Assert.Contains("Descripción: Mostrar {{feedback}} y {{project.name}} tal cual.", prompt);
        Assert.Equal(1, prompt.Split("SECRETO-DE-FEEDBACK").Length - 1);
    }

    [Fact]
    public void Build_UnknownPlaceholders_AreLeftUntouched()
    {
        var stage = new Stage { Kind = StageKind.AGENT, PromptTemplate = "Hola {{no.existe}} de {{task.id}}" };

        var prompt = Builder().Build(ReviewTask(), stage);

        Assert.Equal("Hola {{no.existe}} de 4", prompt);
    }
}
