using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DevArv.Web.Domain;

namespace DevArv.Web.Services;

public static partial class Slug
{
    /// <summary>"Requerimientos de mejora para Fabric" → "requerimientos-de-mejora-para-fabric".</summary>
    public static string Create(string text, int maxLength = 50)
    {
        if (string.IsNullOrWhiteSpace(text)) return "tarea";
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.ToLowerInvariant(c));
        }
        var slug = NonAlnum().Replace(sb.ToString(), "-").Trim('-');
        if (slug.Length > maxLength) slug = slug[..maxLength].TrimEnd('-');
        return slug.Length == 0 ? "tarea" : slug;
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlnum();
}

/// <summary>Etiquetas en español para la UI.</summary>
public static class Labels
{
    public static string Of(WorkStatus s) => s switch
    {
        WorkStatus.Idle => "No iniciado",
        WorkStatus.Queued => "En cola",
        WorkStatus.Running => "En ejecución",
        WorkStatus.WaitingApproval => "Esperando aprobación",
        WorkStatus.Failed => "Error",
        WorkStatus.Done => "Finalizado",
        _ => s.ToString(),
    };

    public static string Of(Priority p) => p switch
    {
        Priority.Low => "Baja",
        Priority.Medium => "Media",
        Priority.High => "Alta",
        Priority.Critical => "Crítica",
        _ => p.ToString(),
    };

    public static string Of(Complexity c) => c switch
    {
        Complexity.Unclassified => "Sin clasificar",
        Complexity.Low => "Baja",
        Complexity.Medium => "Media",
        Complexity.High => "Alta",
        _ => c.ToString(),
    };

    public static string Of(RunStatus s) => s switch
    {
        RunStatus.Queued => "En cola",
        RunStatus.Running => "En ejecución",
        RunStatus.Succeeded => "Completado",
        RunStatus.Failed => "Falló",
        RunStatus.Cancelled => "Cancelado",
        _ => s.ToString(),
    };

    public static string Of(StageKind k) => k switch
    {
        StageKind.Manual => "Manual",
        StageKind.HumanApproval => "Aprobación humana",
        StageKind.Agent => "Agente Claude",
        StageKind.System => "Automática (sistema)",
        StageKind.Terminal => "Final",
        _ => k.ToString(),
    };

    public static string Of(SystemAction a) => a switch
    {
        SystemAction.None => "Ninguna",
        SystemAction.CreatePullRequest => "Crear pull request",
        SystemAction.MergePullRequest => "Hacer merge del pull request",
        _ => a.ToString(),
    };

    public static string Of(CommentKind k) => k switch
    {
        CommentKind.Comment => "Comentario",
        CommentKind.Feedback => "Cambios solicitados",
        CommentKind.ReviewerNote => "Hallazgos del revisor",
        _ => k.ToString(),
    };

    public static string StatusIcon(WorkStatus s) => s switch
    {
        WorkStatus.Queued => "⏳",
        WorkStatus.Running => "●",
        WorkStatus.WaitingApproval => "⏸",
        WorkStatus.Failed => "✖",
        WorkStatus.Done => "✔",
        _ => "○",
    };

    public static string StatusCss(WorkStatus s) => s switch
    {
        WorkStatus.Queued => "st-queued",
        WorkStatus.Running => "st-running",
        WorkStatus.WaitingApproval => "st-waiting",
        WorkStatus.Failed => "st-failed",
        WorkStatus.Done => "st-done",
        _ => "st-idle",
    };

    public static string PriorityCss(Priority p) => p switch
    {
        Priority.Low => "pr-low",
        Priority.High => "pr-high",
        Priority.Critical => "pr-critical",
        _ => "pr-medium",
    };

    public static string Tokens(long tokens) => tokens switch
    {
        >= 1_000_000 => (tokens / 1_000_000d).ToString("0.##", CultureInfo.GetCultureInfo("es-DO")) + " M",
        >= 1_000 => (tokens / 1_000d).ToString("0.#", CultureInfo.GetCultureInfo("es-DO")) + " k",
        _ => tokens.ToString(CultureInfo.InvariantCulture),
    };

    public static string Usd(decimal amount) => "US$ " + amount.ToString("0.00", CultureInfo.InvariantCulture);

    public static string Duration(TimeSpan? d)
    {
        if (d is not { } t) return "";
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h {t.Minutes}m";
        if (t.TotalMinutes >= 1) return $"{(int)t.TotalMinutes}m {t.Seconds}s";
        return $"{Math.Max(0, t.Seconds)}s";
    }

    public static string Initials(string? nameOrEmail)
    {
        if (string.IsNullOrWhiteSpace(nameOrEmail)) return "?";
        var name = nameOrEmail.Split('@')[0];
        var parts = name.Split(['.', '_', '-', ' '], StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2
            ? $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[1][0])}"
            : name[..Math.Min(2, name.Length)].ToUpperInvariant();
    }

    /// <summary>Texto plano corto a partir de Markdown, para las tarjetas.</summary>
    public static string Excerpt(string? markdown, int max = 140)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return "";
        var text = Regex.Replace(markdown, @"[#*_`>\[\]\(\)!-]+", " ");
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return text.Length <= max ? text : text[..max].TrimEnd() + "…";
    }
}

/// <summary>Convierte fechas UTC a la zona horaria configurada (App:TimeZone).</summary>
public sealed class Clock(IConfiguration config)
{
    private readonly TimeZoneInfo _zone = ResolveZone(config["App:TimeZone"]);

    public DateTimeOffset Local(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, _zone);

    public string Format(DateTimeOffset? utc, string format = "dd/MM/yyyy HH:mm") =>
        utc is null ? "" : Local(utc.Value).ToString(format, CultureInfo.InvariantCulture);

    private static TimeZoneInfo ResolveZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return TimeZoneInfo.Local;
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.Local; }
    }
}
