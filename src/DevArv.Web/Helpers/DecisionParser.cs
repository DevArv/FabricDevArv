using System.Text.RegularExpressions;
using DevArv.Web.Enums.Runs;

namespace DevArv.Web.Helpers;

/// <summary>Lee el veredicto que un agente revisor debe escribir al final: "VEREDICTO: APROBADO" o "VEREDICTO: RECHAZADO".</summary>
public static partial class DecisionParser
{
    public static RunDecision Parse(string? ResultText)
    {
        if (string.IsNullOrWhiteSpace(ResultText)) return RunDecision.NONE;

        // Se toma la última coincidencia: el veredicto final manda sobre menciones previas.
        var matches = Verdict().Matches(ResultText);
        if (matches.Count == 0) return RunDecision.NONE;

        var word = matches[^1].Groups["v"].Value.ToUpperInvariant();
        return word.StartsWith("APROB") || word.StartsWith("APPROV")
            ? RunDecision.APPROVED
            : RunDecision.REJECTED;
    }

    [GeneratedRegex(@"(?:VEREDICTO|VERDICT)\**\s*[:：]\s*[\*\s]*(?<v>APROBADO|APROBADA|RECHAZADO|RECHAZADA|APPROVED|REJECTED)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Verdict();
}
