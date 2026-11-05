using DevArv.Contracts.Models;
using DevArv.Web.Constants;
using DevArv.Web.DataModels;
using DevArv.Web.Enums.Tasks;
using DevArv.Web.Enums.Workflow;
using DevArv.Web.Helpers;
using DevArv.Web.Services;
using Microsoft.Extensions.Configuration;

namespace DevArv.Tests.Web.Services;

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

    private static TaskItem ReviewTask() => new()
    {
        Id = 4, Title = "Skill en revisor", Description = "Usar el skill revision.", BaseBranch = "main",
        WorkBranch = "fd_/4-skill", Project = new Project { Name = "Fabric", RepoOwner = "DevArv", RepoName = "FabricDevArv" },
    };

    [Fact]
    public void Build_ReviewerWithSkill_PlacesSkillBlockBeforeReviewSteps()
    {
        var stage = new Stage
        {
            Kind = StageKind.Agent, RequiresDecision = true, PromptTemplate = DefaultPrompts.Reviewer,
            Skill = DefaultPrompts.ReviewerSkill,
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
        var stage = new Stage { Kind = StageKind.Agent, RequiresDecision = true, PromptTemplate = DefaultPrompts.Reviewer };

        var prompt = Builder().Build(ReviewTask(), stage);

        Assert.DoesNotContain("## Skill", prompt);
        Assert.DoesNotContain("{{skill}}", prompt);
    }

    [Fact]
    public void Build_TemplateWithoutSkillVariable_AppendsSkillBlockAtTheEnd()
    {
        var stage = new Stage { Kind = StageKind.Agent, PromptTemplate = "Revisa la tarea {{task.id}}.", Skill = "revision" };

        var prompt = Builder().Build(ReviewTask(), stage);

        Assert.StartsWith("Revisa la tarea 4.", prompt);
        Assert.Contains("## Skill `revision`", prompt);
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
}
