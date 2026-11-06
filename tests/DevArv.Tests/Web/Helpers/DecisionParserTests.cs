using DevArv.Web.Enums.Runs;
using DevArv.Web.Helpers;

namespace DevArv.Tests.Web.Helpers;

public class DecisionParserTests
{
    [Theory]
    [InlineData("Todo bien.\nVEREDICTO: APROBADO", RunDecision.APPROVED)]
    [InlineData("Hallazgos:\n- bug en X\n\nVEREDICTO: RECHAZADO", RunDecision.REJECTED)]
    [InlineData("**Veredicto:** **aprobado**", RunDecision.APPROVED)]
    [InlineData("Veredicto: **APROBADO**", RunDecision.APPROVED)]
    [InlineData("Si fuera grave diría VEREDICTO: RECHAZADO, pero no.\nVEREDICTO: APROBADO", RunDecision.APPROVED)]
    [InlineData("VERDICT: REJECTED", RunDecision.REJECTED)]
    [InlineData("sin veredicto", RunDecision.NONE)]
    [InlineData(null, RunDecision.NONE)]
    public void Parse_UsesLastVerdict(string? Text, RunDecision Expected) =>
        Assert.Equal(Expected, DecisionParser.Parse(Text));
}
