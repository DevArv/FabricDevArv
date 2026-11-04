using DevArv.Contracts.Models;
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
}
