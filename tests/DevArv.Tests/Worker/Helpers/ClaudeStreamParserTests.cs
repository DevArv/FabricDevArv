using DevArv.Worker.Models;
using DevArv.Worker.Helpers;

namespace DevArv.Tests.Worker.Helpers;

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
