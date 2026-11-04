using System.Text;
using DevArv.Contracts.Models;
using DevArv.Web.Constants;
using DevArv.Web.DataModels;
using DevArv.Web.Enums.Tasks;
using DevArv.Web.Helpers;

namespace DevArv.Web.Services;

/// <summary>Arma el prompt final de una etapa de agente a partir de la plantilla y los datos de la tarea.</summary>
public sealed class PromptBuilder(Clock clock)
{
    private const int MaxFeedbackItems = 6;

    /// <param name="task">Tarea con Project, Attachments y Comments cargados.</param>
    public string Build(TaskItem task, Stage stage)
    {
        var template = string.IsNullOrWhiteSpace(stage.PromptTemplate)
            ? (stage.RequiresDecision ? DefaultPrompts.Reviewer : DefaultPrompts.Developer)
            : stage.PromptTemplate;

        var values = new Dictionary<string, string>
        {
            ["task.id"] = task.Id.ToString(),
            ["task.title"] = task.Title,
            ["task.description"] = string.IsNullOrWhiteSpace(task.Description) ? "_(sin descripción)_" : task.Description,
            ["task.priority"] = Labels.Of(task.Priority),
            ["task.complexity"] = Labels.Of(task.Complexity),
            ["task.labels"] = string.IsNullOrWhiteSpace(task.Labels) ? "ninguna" : task.Labels,
            ["project.name"] = task.Project.Name,
            ["repo"] = task.Project.RepoFullName,
            ["baseBranch"] = task.BaseBranch,
            ["workBranch"] = task.WorkBranch ?? "",
            ["attachments"] = BuildAttachments(task),
            ["feedback"] = BuildFeedback(task),
        };

        var sb = new StringBuilder(template);
        foreach (var (key, value) in values)
            sb.Replace("{{" + key + "}}", value);
        return CollapseBlankLines(sb.ToString()).Trim();
    }

    public static string? BuildSystemAppend(TaskItem task, Stage stage)
    {
        var parts = new[] { task.Project.AgentInstructions, stage.AppendSystemPrompt }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim())
            .ToList();
        return parts.Count == 0 ? null : string.Join("\n\n", parts);
    }

    private static string BuildAttachments(TaskItem task)
    {
        if (task.Attachments.Count == 0) return "";
        var sb = new StringBuilder();
        sb.AppendLine("## Archivos adjuntos");
        sb.AppendLine($"Están en `{PromptTokens.AttachmentsDir}`. Ábrelos con la herramienta Read (soporta imágenes y PDF) antes de empezar:");
        foreach (var a in task.Attachments.OrderBy(a => a.Id))
        {
            var info = new AttachmentInfo(a.Id, a.FileName, a.ContentType, a.Size);
            sb.AppendLine($"- `{info.LocalFileName}` ({KindLabel(a)})");
        }
        return sb.ToString();
    }

    private static string KindLabel(TaskAttachment a) => a.Kind switch
    {
        AttachmentKind.Image => "imagen",
        AttachmentKind.Pdf => "PDF",
        AttachmentKind.Html => "HTML de referencia",
        _ => a.ContentType,
    };

    private string BuildFeedback(TaskItem task)
    {
        var items = task.Comments
            .Where(c => c.Kind is CommentKind.Feedback or CommentKind.ReviewerNote)
            .OrderByDescending(c => c.CreatedAt)
            .Take(MaxFeedbackItems)
            .Reverse()
            .ToList();
        if (items.Count == 0) return "";

        var sb = new StringBuilder();
        sb.AppendLine("## Retroalimentación y hallazgos previos (del más antiguo al más reciente)");
        for (var i = 0; i < items.Count; i++)
        {
            var c = items[i];
            var who = c.Kind == CommentKind.ReviewerNote ? "Revisor automático" : c.Author;
            var latest = i == items.Count - 1 ? " — **el más reciente, prioritario**" : "";
            sb.AppendLine($"### {Labels.Of(c.Kind)} · {who} · {clock.Format(c.CreatedAt)}{latest}");
            sb.AppendLine(c.Body.Trim());
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string CollapseBlankLines(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder();
        var blank = 0;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                if (++blank > 1) continue;
            }
            else blank = 0;
            sb.Append(line.TrimEnd()).Append('\n');
        }
        return sb.ToString();
    }
}
