using System.Text;
using System.Text.RegularExpressions;
using DevArv.Contracts.Models;
using DevArv.Web.Constants;
using DevArv.Web.DataModels;
using DevArv.Web.Enums.Tasks;
using DevArv.Web.Helpers;

namespace DevArv.Web.Services;

/// <summary>Arma el prompt final de una etapa de agente a partir de la plantilla y los datos de la tarea.</summary>
public sealed partial class PromptBuilder(Clock Clock)
{
    private const int MAX_FEEDBACK_ITEMS = 6;

    /// <param name="Item">Tarea con Project, Attachments y Comments cargados.</param>
    /// <param name="Stage">Columna de agente cuya plantilla se usa.</param>
    public string Build(TaskItem Item, Stage Stage)
    {
        var template = string.IsNullOrWhiteSpace(Stage.PromptTemplate)
            ? (Stage.RequiresDecision ? DefaultPrompts.REVIEWER : DefaultPrompts.DEVELOPER)
            : Stage.PromptTemplate;

        var values = new Dictionary<string, string>
        {
            ["task.id"] = Item.ID.ToString(),
            ["task.title"] = Item.Title,
            ["task.description"] = string.IsNullOrWhiteSpace(Item.Description) ? "_(sin descripción)_" : Item.Description,
            ["task.priority"] = Labels.Of(Item.Priority),
            ["task.complexity"] = Labels.Of(Item.Complexity),
            ["task.labels"] = string.IsNullOrWhiteSpace(Item.Labels) ? "ninguna" : Item.Labels,
            ["project.name"] = Item.Project.Name,
            ["repo"] = Item.Project.RepoFullName,
            ["baseBranch"] = Item.BaseBranch,
            ["workBranch"] = Item.WorkBranch ?? "",
            ["attachments"] = BuildAttachments(Item),
            ["feedback"] = BuildFeedback(Item),
            ["skill"] = BuildSkill(Stage),
        };

        // Las plantillas anteriores a {{skill}} (ya guardadas en la base) reciben el bloque al final.
        if (!template.Contains("{{skill}}") && values["skill"].Length > 0)
            template = template.TrimEnd() + "\n\n{{skill}}";

        // Una sola pasada: un «{{x}}» escrito dentro de la descripción o de un comentario no se vuelve a expandir.
        var filled = Placeholder().Replace(template, match =>
            values.TryGetValue(match.Groups["key"].Value, out var value) ? value : match.Value);
        return CollapseBlankLines(filled).Trim();
    }

    public static string? BuildSystemAppend(TaskItem Item, Stage Stage)
    {
        var parts = new[] { Item.Project.AgentInstructions, Stage.AppendSystemPrompt }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim())
            .ToList();
        return parts.Count == 0 ? null : string.Join("\n\n", parts);
    }

    /// <summary>
    /// Herramientas permitidas de la etapa (una por línea en la columna). Si la etapa usa un skill, agrega
    /// <c>Skill(nombre)</c> para que el agente pueda invocarlo sin pedir permiso en modo no interactivo.
    /// </summary>
    public static List<string> BuildAllowedTools(Stage Stage)
    {
        var tools = (Stage.AllowedTools ?? "")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (string.IsNullOrWhiteSpace(Stage.Skill)) return tools;

        var skillTool = $"Skill({Stage.Skill.Trim()})";
        if (!tools.Contains("Skill") && !tools.Contains(skillTool)) tools.Add(skillTool);
        return tools;
    }

    private static string BuildSkill(Stage Stage)
    {
        if (string.IsNullOrWhiteSpace(Stage.Skill)) return "";
        var name = Stage.Skill.Trim();
        var builder = new StringBuilder();
        builder.AppendLine($"## Skill `{name}`");
        builder.AppendLine($"Aplica esta etapa con el skill `{name}` del repositorio: antes de empezar, invócalo con la herramienta "
                           + $"Skill (equivale a `/{name}`) y sigue sus pautas y su checklist.");
        builder.AppendLine("Si el skill no está disponible en este repositorio, dilo en tu respuesta y continúa con las pautas de este prompt.");
        builder.AppendLine("Si el skill pide otro formato de respuesta, el formato de este prompt tiene prioridad "
                           + "(en especial la línea final de VEREDICTO, si se pide).");
        return builder.ToString();
    }

    private static string BuildAttachments(TaskItem Item)
    {
        if (Item.Attachments.Count == 0) return "";
        var builder = new StringBuilder();
        builder.AppendLine("## Archivos adjuntos");
        builder.AppendLine($"Están en `{PromptTokens.ATTACHMENTS_DIR}`. Ábrelos con la herramienta Read (soporta imágenes y PDF) antes de empezar:");
        foreach (var attachment in Item.Attachments.OrderBy(a => a.ID))
        {
            var info = new AttachmentInfo(attachment.ID, attachment.FileName, attachment.ContentType, attachment.Size);
            builder.AppendLine($"- `{info.LocalFileName}` ({KindLabel(attachment)})");
        }
        return builder.ToString();
    }

    private static string KindLabel(TaskAttachment Attachment) => Attachment.Kind switch
    {
        AttachmentKind.IMAGE => "imagen",
        AttachmentKind.PDF => "PDF",
        AttachmentKind.HTML => "HTML de referencia",
        _ => Attachment.ContentType,
    };

    private string BuildFeedback(TaskItem Item)
    {
        var items = Item.Comments
            .Where(c => c.Kind is CommentKind.FEEDBACK or CommentKind.REVIEWER_NOTE)
            .OrderByDescending(c => c.CreatedAt)
            .Take(MAX_FEEDBACK_ITEMS)
            .Reverse()
            .ToList();
        if (items.Count == 0) return "";

        var builder = new StringBuilder();
        builder.AppendLine("## Retroalimentación y hallazgos previos (del más antiguo al más reciente)");
        for (var i = 0; i < items.Count; i++)
        {
            var comment = items[i];
            var who = comment.Kind == CommentKind.REVIEWER_NOTE ? "Revisor automático" : comment.Author;
            var latest = i == items.Count - 1 ? " — **el más reciente, prioritario**" : "";
            builder.AppendLine($"### {Labels.Of(comment.Kind)} · {who} · {Clock.Format(comment.CreatedAt)}{latest}");
            builder.AppendLine(comment.Body.Trim());
            builder.AppendLine();
        }
        return builder.ToString();
    }

    private static string CollapseBlankLines(string Text)
    {
        var lines = Text.Replace("\r\n", "\n").Split('\n');
        var builder = new StringBuilder();
        var blank = 0;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                if (++blank > 1) continue;
            }
            else blank = 0;
            builder.Append(line.TrimEnd()).Append('\n');
        }
        return builder.ToString();
    }

    [GeneratedRegex(@"\{\{(?<key>[A-Za-z0-9.]+)\}\}")]
    private static partial Regex Placeholder();
}
