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
    /// <param name="stage">Columna de agente que se ejecuta.</param>
    /// <param name="resolveConflicts">
    /// La ejecución es para arreglar los conflictos del pull request: agrega las instrucciones (variable
    /// <c>{{conflicts}}</c>, o al final si la plantilla no la usa).
    /// </param>
    public string Build(TaskItem task, Stage stage, bool resolveConflicts = false)
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
            ["skill"] = BuildSkill(stage),
            ["conflicts"] = resolveConflicts ? BuildConflicts(task) : "",
        };

        // Las plantillas anteriores a {{skill}} (ya guardadas en la base) reciben el bloque al final.
        if (!template.Contains("{{skill}}") && values["skill"].Length > 0)
            template = template.TrimEnd() + "\n\n{{skill}}";
        // Igual con los conflictos: al final, después de «Cómo trabajar», para que prevalezcan sobre implementar la tarea.
        if (!template.Contains("{{conflicts}}") && values["conflicts"].Length > 0)
            template = template.TrimEnd() + "\n\n{{conflicts}}";

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

    /// <summary>
    /// Herramientas permitidas de la etapa (una por línea en la columna). Si la etapa usa un skill, agrega
    /// <c>Skill(nombre)</c> para que el agente pueda invocarlo sin pedir permiso en modo no interactivo.
    /// </summary>
    public static List<string> BuildAllowedTools(Stage stage)
    {
        var tools = (stage.AllowedTools ?? "")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (string.IsNullOrWhiteSpace(stage.Skill)) return tools;

        var skillTool = $"Skill({stage.Skill.Trim()})";
        if (!tools.Contains("Skill") && !tools.Contains(skillTool)) tools.Add(skillTool);
        return tools;
    }

    private static string BuildSkill(Stage stage)
    {
        if (string.IsNullOrWhiteSpace(stage.Skill)) return "";
        var name = stage.Skill.Trim();
        var sb = new StringBuilder();
        sb.AppendLine($"## Skill `{name}`");
        sb.AppendLine($"Aplica esta etapa con el skill `{name}` del repositorio: antes de empezar, invócalo con la herramienta "
                      + $"Skill (equivale a `/{name}`) y sigue sus pautas y su checklist.");
        sb.AppendLine("Si el skill no está disponible en este repositorio, dilo en tu respuesta y continúa con las pautas de este prompt.");
        sb.AppendLine("Si el skill pide otro formato de respuesta, el formato de este prompt tiene prioridad "
                      + "(en especial la línea final de VEREDICTO, si se pide).");
        return sb.ToString();
    }

    private static string BuildConflicts(TaskItem task)
    {
        var pr = task.PullRequestNumber is { } number ? $"El pull request #{number}" : "El pull request";
        var baseBranch = task.BaseBranch;
        var sb = new StringBuilder();
        sb.AppendLine($"## Arreglar conflictos con `{baseBranch}` (prioritario: reemplaza los pasos anteriores)");
        sb.AppendLine($"{pr} de esta tarea tiene conflictos con `{baseBranch}` en GitHub. La tarea ya está implementada en "
                      + $"`{task.WorkBranch}`: en esta ejecución tu trabajo es **sólo** resolver esos conflictos, sin cambiar "
                      + "el alcance de la tarea.");
        sb.AppendLine($"Antes de iniciarte, el orquestador integró `origin/{baseBranch}` en la rama (`git merge`) y dejó sin "
                      + "resolver los archivos en conflicto (con marcadores `<<<<<<<`, `=======` y `>>>>>>>`).");
        sb.AppendLine("1. Lista los archivos en conflicto con `git diff --name-only --diff-filter=U` (o `git status`).");
        sb.AppendLine($"2. En cada uno combina ambos lados: conserva lo que llegó de `{baseBranch}` y los cambios de esta tarea. "
                      + "Elimina todos los marcadores de conflicto.");
        sb.AppendLine("3. Compila y ejecuta las pruebas; corrige lo que rompa la integración.");
        sb.AppendLine("4. No ejecutes `git merge --abort`, `git reset`, `git rebase` ni cambies de rama: el orquestador hace el "
                      + "commit del merge y el push. Si quedan marcadores de conflicto, no se hará push.");
        sb.AppendLine("5. Si no hay archivos en conflicto (la integración fue limpia), sólo verifica que compile y que pasen las pruebas.");
        sb.AppendLine();
        sb.AppendLine("Termina con un resumen breve en español: archivos que tenían conflictos, cómo resolviste cada uno y cómo se probó.");
        return sb.ToString();
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
