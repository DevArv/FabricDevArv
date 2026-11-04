using DevArv.Contracts.Models;
using DevArv.Worker.Services;

namespace DevArv.Tests.Worker.Services;

public class GitWorkspaceTests
{
    [Fact]
    public void ParsesNameStatusIncludingRenames()
    {
        var files = GitWorkspace.ParseNameStatus("M\tsrc/a.cs\nA\tsrc/b.cs\nR087\told/c.cs\tnew/c.cs\nD\tx.txt\n");
        Assert.Equal(
            new[] { new ChangedFile("M", "src/a.cs"), new ChangedFile("A", "src/b.cs"), new ChangedFile("R", "new/c.cs"), new ChangedFile("D", "x.txt") },
            files);
    }
}
