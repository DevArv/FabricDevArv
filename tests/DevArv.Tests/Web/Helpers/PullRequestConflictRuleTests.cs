using DevArv.Web.DataModels;
using DevArv.Web.Enums.Tasks;
using DevArv.Web.Enums.Workflow;
using DevArv.Web.Helpers;

namespace DevArv.Tests.Web.Helpers;

public class PullRequestConflictRuleTests
{
    private static readonly Stage ProductApproval =
        new() { Id = 7, Name = "Aprobación Producto", Order = 70, Kind = StageKind.HumanApproval };

    private static readonly Stage PendingDeploy =
        new() { Id = 9, Name = "Pendiente Deploy", Order = 90, Kind = StageKind.Manual, IsMergeDestination = true };

    [Theory]
    [InlineData(false, "dirty", true)]
    [InlineData(null, "dirty", true)]
    [InlineData(false, null, true)]
    [InlineData(true, "clean", false)]
    [InlineData(true, "blocked", false)]
    [InlineData(true, "unstable", false)]
    [InlineData(null, "unknown", null)]
    [InlineData(null, null, null)]
    public void FromGitHub_InterpretsMergeableFields(bool? mergeable, string? state, bool? expected) =>
        Assert.Equal(expected, PullRequestConflictRule.FromGitHub(mergeable, state));

    [Fact]
    public void ShouldWatch_WithoutMergeDestination_WatchesEveryNonTerminalStage()
    {
        Assert.True(PullRequestConflictRule.ShouldWatch(ProductApproval, null));
        Assert.True(PullRequestConflictRule.ShouldWatch(PendingDeploy, null));
        Assert.False(PullRequestConflictRule.ShouldWatch(new Stage { Kind = StageKind.Terminal, Order = 5 }, null));
    }

    [Fact]
    public void ShouldWatch_WithMergeDestination_WatchesOnlyStagesBeforeIt()
    {
        Assert.True(PullRequestConflictRule.ShouldWatch(ProductApproval, PendingDeploy));
        Assert.False(PullRequestConflictRule.ShouldWatch(PendingDeploy, PendingDeploy));
        Assert.False(PullRequestConflictRule.ShouldWatch(new Stage { Id = 10, Kind = StageKind.Manual, Order = 100 }, PendingDeploy));
    }

    private static TaskItem ConflictedTask(WorkStatus status = WorkStatus.WaitingApproval) => new()
    {
        Id = 1, PullRequestNumber = 12, PullRequestHasConflicts = true, Status = status, Stage = ProductApproval,
    };

    [Fact]
    public void CanResolve_ConflictedTaskWaitingApproval_ReturnsTrue()
    {
        Assert.True(PullRequestConflictRule.ShowConflicts(ConflictedTask()));
        Assert.True(PullRequestConflictRule.CanResolve(ConflictedTask(), ProductApproval));
        Assert.True(PullRequestConflictRule.CanResolve(ConflictedTask(WorkStatus.Failed), ProductApproval));
    }

    [Theory]
    [InlineData(WorkStatus.Queued)]
    [InlineData(WorkStatus.Running)]
    public void CanResolve_WhileRunning_ReturnsFalse(WorkStatus status)
    {
        var task = ConflictedTask(status);
        Assert.True(PullRequestConflictRule.ShowConflicts(task));
        Assert.False(PullRequestConflictRule.CanResolve(task, ProductApproval));
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

        Assert.False(PullRequestConflictRule.CanResolve(noConflicts, ProductApproval));
        Assert.False(PullRequestConflictRule.CanResolve(merged, ProductApproval));
        Assert.False(PullRequestConflictRule.ShowConflicts(merged));
        Assert.False(PullRequestConflictRule.CanResolve(archived, ProductApproval));
        Assert.False(PullRequestConflictRule.CanResolve(withoutPullRequest, ProductApproval));
        Assert.False(PullRequestConflictRule.CanResolve(ConflictedTask(), new Stage { Kind = StageKind.Terminal }));
    }
}
