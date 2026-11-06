using DevArv.Web.Helpers;

namespace DevArv.Tests.Web.Helpers;

public class PullRequestTitleTests
{
    [Theory]
    [InlineData("fd_/1-prueba-pr", "fd-1-prueba-pr")]
    [InlineData("fd_/5-condiciones-de-nombrado-para-pull-request", "fd-5-condiciones-de-nombrado-para-pull-request")]
    [InlineData("devarv/12-mejora", "devarv-12-mejora")]
    [InlineData("  feature//_x  ", "feature-x")]
    [InlineData("_/3-tarea/", "3-tarea")]
    public void FromBranch_ReplacesSeparatorsWithSingleHyphen(string Branch, string Expected) =>
        Assert.Equal(Expected, PullRequestTitle.FromBranch(Branch));

    [Fact]
    public void FromBranch_WithoutAlphanumerics_ReturnsBranch() =>
        Assert.Equal("_/", PullRequestTitle.FromBranch("_/"));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void FromBranch_EmptyBranch_Throws(string Branch) =>
        Assert.Throws<ArgumentException>(() => PullRequestTitle.FromBranch(Branch));
}
