using DevArv.Web.DataModels;
using DevArv.Web.Enums.Workflow;
using DevArv.Web.Helpers;

namespace DevArv.Tests.Web.Helpers;

public class MergedPullRequestRuleTests
{
    private static readonly Stage _PendingDeploy =
        new() { Id = 9, Name = "Pendiente Deploy", Order = 90, Kind = StageKind.Manual, IsMergeDestination = true };

    [Theory]
    [InlineData(StageKind.HumanApproval, 70)] // Aprobación Producto
    [InlineData(StageKind.HumanApproval, 80)] // Revisión Código Manual
    [InlineData(StageKind.Agent, 40)]         // Desarrollador (se pidieron cambios y luego se hizo merge)
    [InlineData(StageKind.System, 60)]        // Creador de PR
    public void ShouldMove_StageBeforeDestination_ReturnsTrue(StageKind Kind, int Order) =>
        Assert.True(MergedPullRequestRule.ShouldMove(new Stage { Id = 1, Kind = Kind, Order = Order }, _PendingDeploy));

    [Fact]
    public void ShouldMove_AlreadyInDestination_ReturnsFalse() =>
        Assert.False(MergedPullRequestRule.ShouldMove(_PendingDeploy, _PendingDeploy));

    [Fact]
    public void ShouldMove_StageAfterDestination_ReturnsFalse() =>
        Assert.False(MergedPullRequestRule.ShouldMove(
            new Stage { Id = 10, Name = "Pendiente Prueba", Kind = StageKind.Manual, Order = 100 }, _PendingDeploy));

    [Fact]
    public void ShouldMove_TerminalStageBeforeDestination_ReturnsFalse() =>
        Assert.False(MergedPullRequestRule.ShouldMove(
            new Stage { Id = 2, Name = "Descartado", Kind = StageKind.Terminal, Order = 5 }, _PendingDeploy));
}
