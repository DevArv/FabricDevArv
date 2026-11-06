using System.Text.RegularExpressions;
using DevArv.Web.DatabaseConnection;
using DevArv.Web.Enums.Runs;
using DevArv.Web.Enums.Tasks;
using DevArv.Web.Enums.Workflow;

namespace DevArv.Tests.Web.DatabaseConnection;

/// <summary>
/// Los textos de la columna <c>legacy</c> son los que las bases de datos ya existentes tienen guardados: si alguno de
/// estos tests falla, un cambio dejaría ilegibles las filas actuales.
/// </summary>
public class EnumNameStringConverterTests
{
    private static void AssertStoresTheHistoricalTexts<TEnum>(params string[] Legacy) where TEnum : struct, Enum
    {
        var members = Enum.GetValues<TEnum>();

        Assert.Equal(Legacy, members.Select(EnumNameStringConverter<TEnum>.ToDatabase));
        foreach (var (member, text) in members.Zip(Legacy))
            Assert.Equal(member, EnumNameStringConverter<TEnum>.FromDatabase(text));
        Assert.All(members, member => Assert.Matches(new Regex("^[A-Z]+(_[A-Z]+)*$"), member.ToString()));
    }

    [Fact] public void WorkStatus() => AssertStoresTheHistoricalTexts<WorkStatus>("Idle", "Queued", "Running", "WaitingApproval", "Failed", "Done");
    [Fact] public void StageKind() => AssertStoresTheHistoricalTexts<StageKind>("Manual", "HumanApproval", "Agent", "System", "Terminal");
    [Fact] public void SystemAction() => AssertStoresTheHistoricalTexts<SystemAction>("None", "CreatePullRequest", "MergePullRequest");
    [Fact] public void Priority() => AssertStoresTheHistoricalTexts<Priority>("Low", "Medium", "High", "Critical");
    [Fact] public void Complexity() => AssertStoresTheHistoricalTexts<Complexity>("Unclassified", "Low", "Medium", "High");
    [Fact] public void RunStatus() => AssertStoresTheHistoricalTexts<RunStatus>("Queued", "Running", "Succeeded", "Failed", "Cancelled");
    [Fact] public void RunDecision() => AssertStoresTheHistoricalTexts<RunDecision>("None", "Approved", "Rejected");
    [Fact] public void CommentKind() => AssertStoresTheHistoricalTexts<CommentKind>("Comment", "Feedback", "ReviewerNote");
    [Fact] public void AttachmentKind() => AssertStoresTheHistoricalTexts<AttachmentKind>("Image", "Pdf", "Html");

    [Fact]
    public void FromDatabase_UnknownText_ThrowsWithAClearMessage()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => EnumNameStringConverter<Priority>.FromDatabase("Urgent"));

        Assert.Contains("Urgent", ex.Message);
        Assert.Contains(nameof(Priority), ex.Message);
    }

    [Fact]
    public void FromDatabase_IsCaseSensitive_SoCorruptedTextIsNotAcceptedSilently() =>
        Assert.Throws<InvalidOperationException>(() => EnumNameStringConverter<Priority>.FromDatabase("high"));

    [Fact]
    public void ToDatabase_UndefinedValue_KeepsTheNumericTextLikeBefore() =>
        Assert.Equal("99", EnumNameStringConverter<Priority>.ToDatabase((Priority)99));
}
