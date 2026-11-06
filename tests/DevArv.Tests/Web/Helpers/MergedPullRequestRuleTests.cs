using DevArv.Web.DataModels;
using DevArv.Web.Enums.Workflow;
using DevArv.Web.Helpers;

namespace DevArv.Tests.Web.Helpers;

public class MergedPullRequestRuleTests
{
    private static readonly Stage _PendingDeploy =
        new() { ID = 9, Name = "Pendiente Deploy", Order = 90, Kind = StageKind.MANUAL, IsMergeDestination = true };

    [Theory]
    [InlineData(StageKind.HUMAN_APPROVAL, 70)] // Aprobación Producto
    [InlineData(StageKind.HUMAN_APPROVAL, 80)] // Revisión Código Manual
    [InlineData(StageKind.AGENT, 40)]         // Desarrollador (se pidieron cambios y luego se hizo merge)
    [InlineData(StageKind.SYSTEM, 60)]        // Creador de PR
    public void ShouldMove_StageBeforeDestination_ReturnsTrue(StageKind Kind, int Order) =>
        Assert.True(MergedPullRequestRule.ShouldMove(new Stage { ID = 1, Kind = Kind, Order = Order }, _PendingDeploy));

    [Fact]
    public void ShouldMove_AlreadyInDestination_ReturnsFalse() =>
        Assert.False(MergedPullRequestRule.ShouldMove(_PendingDeploy, _PendingDeploy));

    [Fact]
    public void ShouldMove_StageAfterDestination_ReturnsFalse() =>
        Assert.False(MergedPullRequestRule.ShouldMove(
            new Stage { ID = 10, Name = "Pendiente Prueba", Kind = StageKind.MANUAL, Order = 100 }, _PendingDeploy));

    [Fact]
    public void ShouldMove_TerminalStageBeforeDestination_ReturnsFalse() =>
        Assert.False(MergedPullRequestRule.ShouldMove(
            new Stage { ID = 2, Name = "Descartado", Kind = StageKind.TERMINAL, Order = 5 }, _PendingDeploy));
}
