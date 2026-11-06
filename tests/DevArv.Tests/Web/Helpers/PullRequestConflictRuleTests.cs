using DevArv.Web.DataModels;
using DevArv.Web.Enums.Tasks;
using DevArv.Web.Enums.Workflow;
using DevArv.Web.Helpers;

namespace DevArv.Tests.Web.Helpers;

public class PullRequestConflictRuleTests
{
    private static readonly Stage _ProductApproval =
        new() { ID = 7, Name = "Aprobación Producto", Order = 70, Kind = StageKind.HUMAN_APPROVAL };

    private static readonly Stage _PendingDeploy =
        new() { ID = 9, Name = "Pendiente Deploy", Order = 90, Kind = StageKind.MANUAL, IsMergeDestination = true };

    [Theory]
    [InlineData(false, "dirty", true)]
    [InlineData(null, "dirty", true)]
    [InlineData(false, null, true)]
    [InlineData(true, "clean", false)]
    [InlineData(true, "blocked", false)]
    [InlineData(true, "unstable", false)]
    [InlineData(null, "unknown", null)]
    [InlineData(null, null, null)]
    public void FromGitHub_InterpretsMergeableFields(bool? Mergeable, string? State, bool? Expected) =>
        Assert.Equal(Expected, PullRequestConflictRule.FromGitHub(Mergeable, State));

    [Fact]
    public void ShouldWatch_WithoutMergeDestination_WatchesEveryNonTerminalStage()
    {
        Assert.True(PullRequestConflictRule.ShouldWatch(_ProductApproval, null));
        Assert.True(PullRequestConflictRule.ShouldWatch(_PendingDeploy, null));
        Assert.False(PullRequestConflictRule.ShouldWatch(new Stage { Kind = StageKind.TERMINAL, Order = 5 }, null));
    }

    [Fact]
    public void ShouldWatch_WithMergeDestination_WatchesOnlyStagesBeforeIt()
    {
        Assert.True(PullRequestConflictRule.ShouldWatch(_ProductApproval, _PendingDeploy));
        Assert.False(PullRequestConflictRule.ShouldWatch(_PendingDeploy, _PendingDeploy));
        Assert.False(PullRequestConflictRule.ShouldWatch(new Stage { ID = 10, Kind = StageKind.MANUAL, Order = 100 }, _PendingDeploy));
    }

    private static TaskItem ConflictedTask(WorkStatus Status = WorkStatus.WAITING_APPROVAL) => new()
    {
        ID = 1, PullRequestNumber = 12, PullRequestHasConflicts = true, Status = Status, Stage = _ProductApproval,
    };

    [Fact]
    public void CanResolve_ConflictedTaskWaitingApproval_ReturnsTrue()
    {
        Assert.True(PullRequestConflictRule.ShowConflicts(ConflictedTask()));
        Assert.True(PullRequestConflictRule.CanResolve(ConflictedTask(), _ProductApproval));
        Assert.True(PullRequestConflictRule.CanResolve(ConflictedTask(WorkStatus.FAILED), _ProductApproval));
    }

    [Theory]
    [InlineData(WorkStatus.QUEUED)]
    [InlineData(WorkStatus.RUNNING)]
    public void CanResolve_WhileRunning_ReturnsFalse(WorkStatus Status)
    {
        var task = ConflictedTask(Status);
        Assert.True(PullRequestConflictRule.ShowConflicts(task));
        Assert.False(PullRequestConflictRule.CanResolve(task, _ProductApproval));
    }

    [Fact]
    public void CanResolve_WithoutConflictsMergedArchivedOrTerminal_ReturnsFalse()
    {
        var noConflicts = ConflictedTask();
        noConflicts.PullRequestHasConflicts = false;
        var merged = ConflictedTask();
        merged.PullRequestMergedAt = DateTimeOffset.UtcNow;
        var archived = ConflictedTask();
        archived.ArchivedAt = DateTimeOffset.UtcNow;
        var withoutPullRequest = ConflictedTask();
        withoutPullRequest.PullRequestNumber = null;

        Assert.False(PullRequestConflictRule.CanResolve(noConflicts, _ProductApproval));
        Assert.False(PullRequestConflictRule.CanResolve(merged, _ProductApproval));
        Assert.False(PullRequestConflictRule.ShowConflicts(merged));
        Assert.False(PullRequestConflictRule.CanResolve(archived, _ProductApproval));
        Assert.False(PullRequestConflictRule.CanResolve(withoutPullRequest, _ProductApproval));
        Assert.False(PullRequestConflictRule.CanResolve(ConflictedTask(), new Stage { Kind = StageKind.TERMINAL }));
    }
}
