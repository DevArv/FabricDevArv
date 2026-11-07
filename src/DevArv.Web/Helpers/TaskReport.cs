using System.Globalization;
using System.Text.RegularExpressions;
using DevArv.Web.DataModels;
using DevArv.Web.Enums.Runs;
using DevArv.Web.Enums.Workflow;
using DevArv.Web.ViewModels;

namespace DevArv.Web.Helpers;

/// <summary>
/// Pestañas informativas del detalle de la tarea (Log, Pruebas unitarias, Capturas y Pruebas de QA): cuándo se muestran
/// y cómo se leen del resumen final del desarrollador las secciones de pruebas que le pide <c>PromptBuilder</c>.
/// </summary>
public static partial class TaskReport
{
    /// <summary>Título de la sección del resumen con el resultado de las pruebas unitarias.</summary>
    public const string UNIT_TESTS_HEADING = "Pruebas unitarias";

    /// <summary>Título de la sección del resumen con la guía funcional para QA.</summary>
    public const string QA_TESTS_HEADING = "Pruebas QA";

    /// <summary>Títulos aceptados para la sección de QA (el agente a veces escribe «Pruebas de QA»).</summary>
    public static readonly string[] QA_TESTS_HEADINGS = [QA_TESTS_HEADING, "Pruebas de QA"];

    /// <summary>Títulos aceptados para la sección de pruebas unitarias.</summary>
    public static readonly string[] UNIT_TESTS_HEADINGS = [UNIT_TESTS_HEADING];

    /// <summary>
    /// Las pestañas se muestran cuando la tarea llegó a la columna «Creador de PR» (etapa de sistema que abre el pull
    /// request): está en ella, ya tuvo una ejecución en ella o ya tiene pull request.
    /// </summary>
    public static bool ShowTabs(TaskItem Item, IEnumerable<Stage> Stages)
    {
        var pullRequestStageIDs = Stages
            .Where(s => s.Kind == StageKind.SYSTEM && s.SystemAction == SystemAction.CREATE_PULL_REQUEST)
            .Select(s => s.ID)
            .ToHashSet();
        return Item.PullRequestUrl is not null
               || pullRequestStageIDs.Contains(Item.StageID)
               || Item.Runs.Any(r => pullRequestStageIDs.Contains(r.StageID));
    }

    /// <summary>
    /// Busca la sección en el resumen de la ejecución exitosa más reciente del desarrollador (columnas de agente que
    /// hacen push) que la incluya. Las ejecuciones que no la traen (p. ej. el arreglo de conflictos) se saltan.
    /// </summary>
    public static TaskReportSectionViewModel? FindSection(
        IEnumerable<TaskRun> Runs, IEnumerable<Stage> Stages, IReadOnlyCollection<string> Headings)
    {
        var developerStageIDs = Stages
            .Where(s => s.Kind == StageKind.AGENT && s.PushChanges)
            .Select(s => s.ID)
            .ToHashSet();
        var candidates = Runs
            .Where(r => r.Status == RunStatus.SUCCEEDED
                        && r.RunnerKind == StageKind.AGENT
                        && developerStageIDs.Contains(r.StageID))
            .OrderByDescending(r => r.ID);

        foreach (var run in candidates)
        {
            var content = ExtractSection(
                Markdown: run.Summary,
                Headings: Headings);
            if (content is not null)
                return new TaskReportSectionViewModel
                {
                    Content = content,
                    RunID = run.ID,
                    StageName = run.StageName,
                    FinishedAt = run.FinishedAt,
                };
        }
        return null;
    }

    /// <summary>
    /// Contenido de la sección Markdown cuyo título (de cualquier nivel, sin distinguir mayúsculas ni acentos y sin los
    /// «:» o el énfasis finales) es uno de <paramref name="Headings"/>, hasta el siguiente título del mismo nivel o
    /// superior. Ignora los títulos dentro de bloques de código. Null si no existe o está vacía.
    /// </summary>
    public static string? ExtractSection(string? Markdown, IReadOnlyCollection<string> Headings)
    {
        if (string.IsNullOrWhiteSpace(Markdown)) return null;

        var lines = Markdown.Replace("\r\n", "\n").Split('\n');
        var inFence = false;
        int? level = null;
        var content = new List<string>();

        foreach (var line in lines)
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal) || line.TrimStart().StartsWith("~~~", StringComparison.Ordinal))
            {
                inFence = !inFence;
                if (level is not null) content.Add(line);
                continue;
            }

            var heading = inFence ? null : HeadingRegex().Match(line);
            if (heading is { Success: true })
            {
                var headingLevel = heading.Groups["hashes"].Value.Length;
                if (level is not null && headingLevel <= level) break;
                if (level is null)
                {
                    if (Headings.Any(h => SameTitle(
                            Title: heading.Groups["title"].Value,
                            Expected: h))) level = headingLevel;
                    continue;
                }
            }

            if (level is not null) content.Add(line);
        }

        if (level is null) return null;
        var text = string.Join('\n', content).Trim();
        return text.Length == 0 ? null : text;
    }

    /// <summary>Cantidad de líneas de un log (la línea final vacía no cuenta).</summary>
    public static int CountLines(string? Text)
    {
        if (string.IsNullOrEmpty(Text)) return 0;
        var lines = Text.Count(c => c == '\n');
        return Text.EndsWith('\n') ? lines : lines + 1;
    }

    private static bool SameTitle(string Title, string Expected)
    {
        var clean = Title.Trim().Trim('*', '_', ' ').TrimEnd(':').Trim('*', '_', ' ');
        return string.Compare(
            strA: clean,
            strB: Expected,
            culture: CultureInfo.InvariantCulture,
            options: CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) == 0;
    }

    [GeneratedRegex(@"^ {0,3}(?<hashes>#{1,6})[ \t]+(?<title>.*?)[ \t]*#*[ \t]*$")]
    private static partial Regex HeadingRegex();
}
