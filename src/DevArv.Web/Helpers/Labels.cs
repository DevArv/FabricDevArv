using System.Globalization;
using System.Text.RegularExpressions;
using DevArv.Web.Enums.Runs;
using DevArv.Web.Enums.Tasks;
using DevArv.Web.Enums.Workflow;

namespace DevArv.Web.Helpers;

/// <summary>Etiquetas en español para la UI.</summary>
public static class Labels
{
    public static string Of(WorkStatus Status) => Status switch
    {
        WorkStatus.Idle => "No iniciado",
        WorkStatus.Queued => "En cola",
        WorkStatus.Running => "En ejecución",
        WorkStatus.WaitingApproval => "Esperando aprobación",
        WorkStatus.Failed => "Error",
        WorkStatus.Done => "Finalizado",
        _ => Status.ToString(),
    };

    public static string Of(Priority Priority) => Priority switch
    {
        Priority.Low => "Baja",
        Priority.Medium => "Media",
        Priority.High => "Alta",
        Priority.Critical => "Crítica",
        _ => Priority.ToString(),
    };

    public static string Of(Complexity Complexity) => Complexity switch
    {
        Complexity.Unclassified => "Sin clasificar",
        Complexity.Low => "Baja",
        Complexity.Medium => "Media",
        Complexity.High => "Alta",
        _ => Complexity.ToString(),
    };

    public static string Of(RunStatus Status) => Status switch
    {
        RunStatus.Queued => "En cola",
        RunStatus.Running => "En ejecución",
        RunStatus.Succeeded => "Completado",
        RunStatus.Failed => "Falló",
        RunStatus.Cancelled => "Cancelado",
        _ => Status.ToString(),
    };

    public static string Of(StageKind Kind) => Kind switch
    {
        StageKind.Manual => "Manual",
        StageKind.HumanApproval => "Aprobación humana",
        StageKind.Agent => "Agente Claude",
        StageKind.System => "Automática (sistema)",
        StageKind.Terminal => "Final",
        _ => Kind.ToString(),
    };

    public static string Of(SystemAction a) => a switch
    {
        SystemAction.None => "Ninguna",
        SystemAction.CreatePullRequest => "Crear pull request",
        SystemAction.MergePullRequest => "Hacer merge del pull request",
        _ => a.ToString(),
    };

    public static string Of(CommentKind Kind) => Kind switch
    {
        CommentKind.Comment => "Comentario",
        CommentKind.Feedback => "Cambios solicitados",
        CommentKind.ReviewerNote => "Hallazgos del revisor",
        _ => Kind.ToString(),
    };

    public static string StatusIcon(WorkStatus Status) => Status switch
    {
        WorkStatus.Queued => "⏳",
        WorkStatus.Running => "●",
        WorkStatus.WaitingApproval => "⏸",
        WorkStatus.Failed => "✖",
        WorkStatus.Done => "✔",
        _ => "○",
    };

    public static string StatusCss(WorkStatus Status) => Status switch
    {
        WorkStatus.Queued => "st-queued",
        WorkStatus.Running => "st-running",
        WorkStatus.WaitingApproval => "st-waiting",
        WorkStatus.Failed => "st-failed",
        WorkStatus.Done => "st-done",
        _ => "st-idle",
    };

    public static string PriorityCss(Priority Priority) => Priority switch
    {
        Priority.Low => "pr-low",
        Priority.High => "pr-high",
        Priority.Critical => "pr-critical",
        _ => "pr-medium",
    };

    public static string Tokens(long Tokens) => Tokens switch
    {
        >= 1_000_000 => (Tokens / 1_000_000d).ToString("0.##", CultureInfo.GetCultureInfo("es-DO")) + " M",
        >= 1_000 => (Tokens / 1_000d).ToString("0.#", CultureInfo.GetCultureInfo("es-DO")) + " k",
        _ => Tokens.ToString(CultureInfo.InvariantCulture),
    };

    public static string Usd(decimal Amount) => "US$ " + Amount.ToString("0.00", CultureInfo.InvariantCulture);

    public static string Duration(TimeSpan? d)
    {
        if (d is not { } t) return "";
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h {t.Minutes}m";
        if (t.TotalMinutes >= 1) return $"{(int)t.TotalMinutes}m {t.Seconds}s";
        return $"{Math.Max(0, t.Seconds)}s";
    }

    /// <summary>Cronómetro para las tarjetas: "05:07" o, desde una hora, "1:05:07". Debe coincidir con site.js.</summary>
    public static string Stopwatch(TimeSpan Elapsed)
    {
        var total = Math.Max(0L, (long)Elapsed.TotalSeconds);
        var hours = total / 3600;
        var minutes = total / 60 % 60;
        var seconds = total % 60;
        return hours > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{hours}:{minutes:00}:{seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes:00}:{seconds:00}");
    }

    public static string Initials(string? NameOrEmail)
    {
        if (string.IsNullOrWhiteSpace(NameOrEmail)) return "?";
        var name = NameOrEmail.Split('@')[0];
        var parts = name.Split(['.', '_', '-', ' '], StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2
            ? $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[1][0])}"
            : name[..Math.Min(2, name.Length)].ToUpperInvariant();
    }

    /// <summary>Texto plano corto a partir de Markdown, para las tarjetas.</summary>
    public static string Excerpt(string? Markdown, int Max = 140)
    {
        if (string.IsNullOrWhiteSpace(Markdown)) return "";
        var text = Regex.Replace(Markdown, @"[#*_`>\[\]\(\)!-]+", " ");
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return text.Length <= Max ? text : text[..Max].TrimEnd() + "…";
    }
}
