using DevArv.Contracts;
using DevArv.Worker.Agents;

namespace DevArv.Tests;

public class ClaudeStreamParserTests
{
    [Fact]
    public void ParsesInitToolUseAndResult()
    {
        var parser = new ClaudeStreamParser();
        var log = new List<string>();
        string[] lines =
        [
            """{"type":"system","subtype":"init","session_id":"s1","model":"claude-opus-x","permissionMode":"acceptEdits","tools":["Read"]}""",
            """{"type":"assistant","message":{"model":"claude-opus-x","content":[{"type":"text","text":"Voy a revisar el módulo."},{"type":"tool_use","id":"t1","name":"Bash","input":{"command":"dotnet test"}}]}}""",
            """{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"t1","content":"ok\nPassed!","is_error":false}]}}""",
            """{"type":"result","subtype":"success","is_error":false,"num_turns":7,"result":"Listo.\nVEREDICTO: APROBADO","session_id":"s1","total_cost_usd":0.1534,"usage":{"input_tokens":1200,"output_tokens":800,"cache_read_input_tokens":50000,"cache_creation_input_tokens":3000}}""",
        ];
        foreach (var line in lines) log.AddRange(parser.Parse(line));

        Assert.Contains(log, l => l.Contains("claude-opus-x"));
        Assert.Contains(log, l => l.Contains("🔧 Bash: dotnet test"));
        Assert.Contains(log, l => l.Contains("2 línea(s)"));
        var r = Assert.IsType<ClaudeResult>(parser.Result);
        Assert.False(r.IsError);
        Assert.Equal(0.1534m, r.CostUsd);
        Assert.Equal(1200, r.InputTokens);
        Assert.Equal(800, r.OutputTokens);
        Assert.Equal(50000, r.CacheReadTokens);
        Assert.Equal(3000, r.CacheWriteTokens);
        Assert.Equal(7, r.NumTurns);
        Assert.EndsWith("VEREDICTO: APROBADO", r.ResultText);
    }

    [Fact]
    public void MaxTurnsIsAnError()
    {
        var parser = new ClaudeStreamParser();
        parser.Parse("""{"type":"result","subtype":"error_max_turns","is_error":false,"num_turns":40,"total_cost_usd":1.2}""").ToList();
        Assert.True(parser.Result!.IsError);
    }

    [Fact]
    public void NonJsonLinesPassThrough() =>
        Assert.Equal(new[] { "Error: not logged in" }, new ClaudeStreamParser().Parse("Error: not logged in"));
}

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

public class ClaudeArgumentsTests
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

        var args = DevArv.Worker.Agents.ClaudeRunner.BuildArguments(job, ws);

        Assert.Equal(new[] { "-p", "--output-format", "stream-json", "--verbose" }, args.Take(4));
        Assert.Equal("sonnet", args[args.IndexOf("--model") + 1]);
        Assert.Equal("default", args[args.IndexOf("--permission-mode") + 1]);
        Assert.Equal("30", args[args.IndexOf("--max-turns") + 1]);
        Assert.Equal("/att", args[args.IndexOf("--add-dir") + 1]);
        Assert.True(args.IndexOf("--allowedTools") > args.IndexOf("--add-dir"));
        Assert.Equal(new[] { "--disallowedTools", "Edit" }, args.TakeLast(2));
    }
}
