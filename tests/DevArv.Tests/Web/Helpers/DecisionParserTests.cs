using DevArv.Web.Enums.Runs;
using DevArv.Web.Helpers;

namespace DevArv.Tests.Web.Helpers;

public class DecisionParserTests
{
    [Theory]
    [InlineData("Todo bien.\nVEREDICTO: APROBADO", RunDecision.Approved)]
    [InlineData("Hallazgos:\n- bug en X\n\nVEREDICTO: RECHAZADO", RunDecision.Rejected)]
    [InlineData("**Veredicto:** **aprobado**", RunDecision.Approved)]
    [InlineData("Veredicto: **APROBADO**", RunDecision.Approved)]
    [InlineData("Si fuera grave diría VEREDICTO: RECHAZADO, pero no.\nVEREDICTO: APROBADO", RunDecision.Approved)]
    [InlineData("VERDICT: REJECTED", RunDecision.Rejected)]
    [InlineData("sin veredicto", RunDecision.None)]
    [InlineData(null, RunDecision.None)]
    public void Parse_UsesLastVerdict(string? text, RunDecision expected) =>
        Assert.Equal(expected, DecisionParser.Parse(text));
}
