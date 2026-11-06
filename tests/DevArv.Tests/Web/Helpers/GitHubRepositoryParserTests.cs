using DevArv.Web.Helpers;

namespace DevArv.Tests.Web.Helpers;

public class GitHubRepositoryParserTests
{
    [Theory]
    [InlineData("mi-org/mi-repo", "mi-org", "mi-repo")]
    [InlineData("https://github.com/mi-org/mi-repo", "mi-org", "mi-repo")]
    [InlineData("https://github.com/mi-org/mi-repo.git", "mi-org", "mi-repo")]
    [InlineData("git@github.com:mi-org/mi.repo.git", "mi-org", "mi.repo")]
    public void Parses(string Input, string Owner, string Repo)
    {
        Assert.True(GitHubRepositoryParser.TryParse(Input, out var o, out var r));
        Assert.Equal(Owner, o);
        Assert.Equal(Repo, r);
    }

    [Theory]
    [InlineData("https://gitlab.com/a/b")]
    [InlineData("solo-un-nombre")]
    [InlineData("a/b/c")]
    public void RejectsInvalid(string Input) => Assert.False(GitHubRepositoryParser.TryParse(Input, out _, out _));
}
