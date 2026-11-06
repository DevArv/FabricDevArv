using System.Text.Json;
using DevArv.Contracts.Models;

namespace DevArv.Tests.Contracts.Models;

/// <summary>
/// El JSON de /api/worker es el contrato entre el servidor y los workers instalados en otras PCs, que pueden estar en
/// otra versión. En C# los miembros se llaman <c>RunID</c>, <c>TaskID</c>…, pero los nombres del JSON no pueden cambiar.
/// </summary>
public class WireFormatTests
{
    private static readonly JsonSerializerOptions[] _Options =
    [
        new(JsonSerializerDefaults.Web),   // lo que usan el servidor y el worker
        new(),                             // sin configuración: el nombre debe venir del atributo, no de la política
    ];

    private static HashSet<string> KeysOf(object Value, JsonSerializerOptions Options)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(Value, Options));
        return document.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet();
    }

    private static AgentJob NewJob() => new(
        RunID: 5, TaskID: 7, TaskTitle: "t", StageName: "s", Repo: new RepoInfo("o", "r", "https://github.com/o/r.git"),
        BaseBranch: "main", WorkBranch: "devarv/7-x", Prompt: "p", AppendSystemPrompt: null, Model: null,
        PermissionMode: "default", AllowedTools: [], DisallowedTools: [], MaxTurns: null, ExtraArgs: [], PushChanges: true,
        TimeoutMinutes: 60, CommitMessage: "m", Attachments: [new AttachmentInfo(3, "a.png", "image/png", 12)]);

    [Fact]
    public void AgentJob_KeepsTheJsonNamesRunIDAndTaskID()
    {
        foreach (var options in _Options)
        {
            var keys = KeysOf(NewJob(), options);

            Assert.Contains("runId", keys);
            Assert.Contains("taskId", keys);
            Assert.DoesNotContain("runID", keys);
            Assert.DoesNotContain("RunID", keys);
            Assert.DoesNotContain("taskID", keys);
        }
    }

    [Fact]
    public void AttachmentInfo_KeepsTheJsonNameID_AndStillPublishesLocalFileName()
    {
        foreach (var options in _Options)
        {
            var keys = KeysOf(new AttachmentInfo(3, "a.png", "image/png", 12), options);

            Assert.Contains("id", keys);
            Assert.DoesNotContain("ID", keys);
            Assert.Contains("localFileName", keys.Select(k => char.ToLowerInvariant(k[0]) + k[1..]));
        }
    }

    [Fact]
    public void RunCompletion_KeepsTheJsonNameSessionID()
    {
        var completion = new RunCompletion(true, false, null, "ok", 0.5m, 1, 2, 3, 4, 2, "sesion-1", "opus", "abc", true, []);

        foreach (var options in _Options)
        {
            var keys = KeysOf(completion, options);

            Assert.Contains("sessionId", keys);
            Assert.DoesNotContain("sessionID", keys);
            Assert.DoesNotContain("SessionID", keys);
        }
    }

    [Fact]
    public void AgentJob_ReadsThePayloadAnOlderServerSends()
    {
        const string olderServerPayload = """
            {"runId":5,"taskId":7,"taskTitle":"t","stageName":"s","repo":{"owner":"o","name":"r","cloneUrl":"u"},
             "baseBranch":"main","workBranch":"w","prompt":"p","appendSystemPrompt":null,"model":null,"permissionMode":"default",
             "allowedTools":[],"disallowedTools":[],"maxTurns":null,"extraArgs":[],"pushChanges":true,"timeoutMinutes":60,
             "commitMessage":"m","attachments":[{"id":3,"fileName":"a.png","contentType":"image/png","size":12,"localFileName":"3-a.png"}]}
            """;

        var job = JsonSerializer.Deserialize<AgentJob>(olderServerPayload, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Assert.Equal(5, job.RunID);
        Assert.Equal(7, job.TaskID);
        Assert.Equal(3, job.Attachments.Single().ID);
        Assert.Equal("3-a.png", job.Attachments.Single().LocalFileName);
    }

    [Fact]
    public void RunCompletion_ReadsThePayloadAnOlderWorkerSends()
    {
        const string olderWorkerPayload = """
            {"success":true,"cancelled":false,"errorMessage":null,"resultText":"Listo","costUsd":0.25,"inputTokens":10,
             "outputTokens":20,"cacheReadTokens":0,"cacheWriteTokens":0,"numTurns":3,"sessionId":"s-1","model":"opus",
             "headCommit":"abc","pushed":true,"changedFiles":[{"status":"M","path":"src/A.cs"}]}
            """;

        var completion = JsonSerializer.Deserialize<RunCompletion>(olderWorkerPayload, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Assert.Equal("s-1", completion.SessionID);
        Assert.Equal("src/A.cs", completion.ChangedFiles.Single().Path);
    }
}
