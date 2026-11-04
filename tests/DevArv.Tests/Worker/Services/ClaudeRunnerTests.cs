using DevArv.Contracts.Models;
using DevArv.Worker.Models;
using DevArv.Worker.Services;

namespace DevArv.Tests.Worker.Services;

public class ClaudeRunnerTests
{
    [Fact]
    public void BuildsHeadlessArgumentsWithVariadicListsLast()
    {
        var job = new AgentJob(
            RunId: 1, TaskId: 2, TaskTitle: "t", StageName: "Revisor", Repo: new RepoInfo("o", "r", "https://github.com/o/r.git"),
            BaseBranch: "main", WorkBranch: "devarv/2-t", Prompt: "p", AppendSystemPrompt: "extra", Model: "sonnet",
            PermissionMode: "default", AllowedTools: ["Read", "Bash(git diff:*)"], DisallowedTools: ["Edit"], MaxTurns: 30,
            ExtraArgs: [], PushChanges: false, TimeoutMinutes: 30, CommitMessage: "c",
            Attachments: [new AttachmentInfo(1, "a.png", "image/png", 10)]);
        var ws = new PreparedWorkspace("/w", "/att", "abc", false);

        var args = ClaudeRunner.BuildArguments(job, ws);

        Assert.Equal(new[] { "-p", "--output-format", "stream-json", "--verbose" }, args.Take(4));
        Assert.Equal("sonnet", args[args.IndexOf("--model") + 1]);
        Assert.Equal("default", args[args.IndexOf("--permission-mode") + 1]);
        Assert.Equal("30", args[args.IndexOf("--max-turns") + 1]);
        Assert.Equal("/att", args[args.IndexOf("--add-dir") + 1]);
        Assert.True(args.IndexOf("--allowedTools") > args.IndexOf("--add-dir"));
        Assert.Equal(new[] { "--disallowedTools", "Edit" }, args.TakeLast(2));
    }
}
