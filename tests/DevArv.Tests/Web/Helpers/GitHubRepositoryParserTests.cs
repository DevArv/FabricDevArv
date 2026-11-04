using DevArv.Web.Helpers;

namespace DevArv.Tests.Web.Helpers;

public class GitHubRepositoryParserTests
{
    [Theory]
    [InlineData("mi-org/mi-repo", "mi-org", "mi-repo")]
    [InlineData("https://github.com/mi-org/mi-repo", "mi-org", "mi-repo")]
    [InlineData("https://github.com/mi-org/mi-repo.git", "mi-org", "mi-repo")]
    [InlineData("git@github.com:mi-org/mi.repo.git", "mi-org", "mi.repo")]
    public void Parses(string input, string owner, string repo)
    {
        Assert.True(GitHubRepositoryParser.TryParse(input, out var o, out var r));
        Assert.Equal(owner, o);
        Assert.Equal(repo, r);
    }

    [Theory]
    [InlineData("https://gitlab.com/a/b")]
    [InlineData("solo-un-nombre")]
    [InlineData("a/b/c")]
    public void RejectsInvalid(string input) => Assert.False(GitHubRepositoryParser.TryParse(input, out _, out _));
}
