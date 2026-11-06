using System.Net;
using System.Text.Json;
using DevArv.Contracts.Models;
using DevArv.Tests.TestSupport;
using DevArv.Web.DataModels;
using DevArv.Web.DatabaseConnection;
using DevArv.Web.Enums.Runs;
using DevArv.Web.Enums.Workflow;
using DevArv.Web.Models;
using DevArv.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DevArv.Tests.Web.Services;

public sealed class SystemActionExecutorTests
{
    private const string OPEN_PULL_REQUEST = """{"number":7,"html_url":"https://github.com/acme/fabric/pull/7","state":"open","merged":false}""";
    private const string CLOSED_PULL_REQUEST = """{"number":7,"html_url":"https://github.com/acme/fabric/pull/7","state":"closed","merged":false}""";
    private const string MERGED_PULL_REQUEST = """{"number":7,"html_url":"https://github.com/acme/fabric/pull/7","state":"closed","merged":true}""";
    private const string NEW_PULL_REQUEST = """{"number":9,"html_url":"https://github.com/acme/fabric/pull/9","state":"open","merged":false}""";

    private readonly string _DatabaseName = Guid.NewGuid().ToString("N");

    private AppDbContext CreateDbContext() => TestDatabase.CreateContext(_DatabaseName);

    private static SystemActionExecutor CreateExecutor(AppDbContext Db, StubHttpHandler Handler, string PublicUrl = "http://app/")
    {
        var github = new GitHubService(new HttpClient(Handler), Options.Create(new GitHubOptions { Token = "t" }));
        return new SystemActionExecutor(Db, github, Options.Create(new AppOptions { PublicUrl = PublicUrl }));
    }

    /// <summary>Crea la tarea con una ejecución de sistema y devuelve la ejecución con tarea, proyecto y columna cargados.</summary>
    private async Task<TaskRun> SeedRunAsync(
        SystemAction Action, string? WorkBranch = "devarv/1-mi-tarea", int? PullRequestNumber = null, string? DevSummary = null)
    {
        await using (var db = CreateDbContext())
        {
            db.Projects.Add(new Project { ID = 1, Name = "Fabric", RepoOwner = "acme", RepoName = "fabric" });
            db.Stages.AddRange(
                new Stage { ID = 1, Name = "Desarrollador", Kind = StageKind.AGENT, PushChanges = true },
                new Stage { ID = 2, Name = "Sistema", Kind = StageKind.SYSTEM, SystemAction = Action });
            await db.SaveChangesAsync();
            db.Tasks.Add(new TaskItem
            {
                ID = 1, Title = "Mi tarea", Description = "Detalle", ProjectID = 1, StageID = 2, BaseBranch = "main",
                WorkBranch = WorkBranch, PullRequestNumber = PullRequestNumber,
            });
            await db.SaveChangesAsync();
            if (DevSummary is not null)
                db.TaskRuns.Add(new TaskRun { TaskID = 1, StageID = 1, RunnerKind = StageKind.AGENT, Status = RunStatus.SUCCEEDED, Summary = DevSummary });
            db.TaskRuns.Add(new TaskRun { ID = 100, TaskID = 1, StageID = 2, RunnerKind = StageKind.SYSTEM, Status = RunStatus.RUNNING });
            await db.SaveChangesAsync();
        }

        await using var load = CreateDbContext();
        return await load.TaskRuns.Include(r => r.Task).ThenInclude(t => t.Project).SingleAsync(r => r.ID == 100);
    }

    private async Task<(RunCompletion Result, StubHttpHandler Handler, TaskItem Task)> ExecuteAsync(
        SystemAction Action, Func<CapturedRequest, HttpResponseMessage> Respond, string? WorkBranch = "devarv/1-mi-tarea",
        int? PullRequestNumber = null, string? DevSummary = null, string PublicUrl = "http://app/")
    {
        var run = await SeedRunAsync(
            Action: Action,
            WorkBranch: WorkBranch,
            PullRequestNumber: PullRequestNumber,
            DevSummary: DevSummary);
        var handler = new StubHttpHandler(Respond);
        await using var db = CreateDbContext();
        db.Attach(run.Task);
        var completion = await CreateExecutor(
            Db: db,
            Handler: handler,
            PublicUrl: PublicUrl).ExecuteAsync(
            Run: run,
            CT: CancellationToken.None);
        return (completion, handler, run.Task);
    }

    // ------------------------------------------------------------------ crear el pull request

    [Fact]
    public async Task CreatePullRequest_WithoutWorkBranch_Fails()
    {
        var (result, handler, _) = await ExecuteAsync(
            Action: SystemAction.CREATE_PULL_REQUEST,
            Respond: _ => throw new InvalidOperationException(),
            WorkBranch: null);

        Assert.False(result.Success);
        Assert.Contains("no tiene rama de trabajo", result.ErrorMessage);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task CreatePullRequest_WhenTheBranchWasNeverPushed_FailsWithoutCreatingAnything()
    {
        var (result, handler, _) = await ExecuteAsync(
            Action: SystemAction.CREATE_PULL_REQUEST,
            Respond: _ => StubHttpHandler.Json(HttpStatusCode.NotFound, """{"message":"Branch not found"}"""));

        Assert.False(result.Success);
        Assert.Contains("devarv/1-mi-tarea no existe en GitHub", result.ErrorMessage);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task CreatePullRequest_Creates_ItSetsTheTaskFieldsAndBuildsTheBody()
    {
        var (result, handler, task) = await ExecuteAsync(
            Action: SystemAction.CREATE_PULL_REQUEST,
            Respond: request => request.PathAndQuery switch
            {
                var p when p.Contains("/branches/") => StubHttpHandler.Json(HttpStatusCode.OK, "{}"),
                var p when p.Contains("/pulls?state=open") => StubHttpHandler.Json(HttpStatusCode.OK, "[]"),
                _ => StubHttpHandler.Json(HttpStatusCode.Created, NEW_PULL_REQUEST),
            },
            DevSummary: "Agregué el botón.");

        Assert.True(result.Success);
        Assert.Contains("Pull request creado", result.ResultText);
        Assert.Equal(9, task.PullRequestNumber);
        Assert.Equal("https://github.com/acme/fabric/pull/9", task.PullRequestUrl);
        Assert.Null(task.PullRequestMergedAt);

        var post = Assert.Single(handler.Requests, r => r.Method == HttpMethod.Post);
        using var json = JsonDocument.Parse(post.Body);
        Assert.Equal("devarv-1-mi-tarea", json.RootElement.GetProperty("title").GetString());
        Assert.Equal("devarv/1-mi-tarea", json.RootElement.GetProperty("head").GetString());
        Assert.Equal("main", json.RootElement.GetProperty("base").GetString());
        var body = json.RootElement.GetProperty("body").GetString()!;
        Assert.Contains("## Tarea #1: Mi tarea", body);
        Assert.Contains("Agregué el botón.", body);
        Assert.Contains("[Ver tarea en DevArv](http://app/?task=1)", body);
    }

    [Fact]
    public async Task CreatePullRequest_WithoutPublicUrlOrDeveloperSummary_OmitsTheLinkAndUsesAPlaceholder()
    {
        var (_, handler, _) = await ExecuteAsync(
            Action: SystemAction.CREATE_PULL_REQUEST,
            Respond: request => request.PathAndQuery switch
            {
                var p when p.Contains("/branches/") => StubHttpHandler.Json(HttpStatusCode.OK, "{}"),
                var p when p.Contains("/pulls?state=open") => StubHttpHandler.Json(HttpStatusCode.OK, "[]"),
                _ => StubHttpHandler.Json(HttpStatusCode.Created, NEW_PULL_REQUEST),
            },
            PublicUrl: "");

        var post = Assert.Single(handler.Requests, r => r.Method == HttpMethod.Post);
        var body = JsonDocument.Parse(post.Body).RootElement.GetProperty("body").GetString()!;
        Assert.DoesNotContain("Ver tarea en DevArv", body);
        Assert.Contains("_(sin resumen)_", body);
    }

    [Fact]
    public async Task CreatePullRequest_VeryLongBody_IsCutBelowGitHubsLimit()
    {
        // El resumen del agente no tiene tope, así que es lo que puede pasar el límite de GitHub.
        var (_, handler, _) = await ExecuteAsync(
            Action: SystemAction.CREATE_PULL_REQUEST,
            Respond: request => request.PathAndQuery switch
            {
                var p when p.Contains("/branches/") => StubHttpHandler.Json(HttpStatusCode.OK, "{}"),
                var p when p.Contains("/pulls?state=open") => StubHttpHandler.Json(HttpStatusCode.OK, "[]"),
                _ => StubHttpHandler.Json(HttpStatusCode.Created, NEW_PULL_REQUEST),
            },
            DevSummary: new string('r', 100_000));

        var post = Assert.Single(handler.Requests, r => r.Method == HttpMethod.Post);
        var body = JsonDocument.Parse(post.Body).RootElement.GetProperty("body").GetString()!;
        Assert.True(body.Length < 65_536);
        Assert.EndsWith("_(recortado)_", body);
    }

    [Fact]
    public async Task CreatePullRequest_ExistingOpenPullRequest_IsKeptAndNothingIsCreated()
    {
        var (result, handler, _) = await ExecuteAsync(
            Action: SystemAction.CREATE_PULL_REQUEST,
            Respond: _ => StubHttpHandler.Json(HttpStatusCode.OK, OPEN_PULL_REQUEST),
            PullRequestNumber: 7);

        Assert.True(result.Success);
        Assert.Contains("ya existe y sigue abierto", result.ResultText);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task CreatePullRequest_ClosedPullRequestOfTheTask_CreatesANewOne()
    {
        var (result, _, task) = await ExecuteAsync(
            Action: SystemAction.CREATE_PULL_REQUEST,
            Respond: request => request.PathAndQuery switch
            {
                "/repos/acme/fabric/pulls/7" => StubHttpHandler.Json(HttpStatusCode.OK, CLOSED_PULL_REQUEST),
                var p when p.Contains("/branches/") => StubHttpHandler.Json(HttpStatusCode.OK, "{}"),
                var p when p.Contains("/pulls?state=open") => StubHttpHandler.Json(HttpStatusCode.OK, "[]"),
                _ => StubHttpHandler.Json(HttpStatusCode.Created, NEW_PULL_REQUEST),
            },
            PullRequestNumber: 7);

        Assert.True(result.Success);
        Assert.Equal(9, task.PullRequestNumber);
    }

    [Fact]
    public async Task CreatePullRequest_AnotherOpenPullRequestOnTheBranch_IsReused()
    {
        var (result, handler, task) = await ExecuteAsync(
            Action: SystemAction.CREATE_PULL_REQUEST,
            Respond: request => request.PathAndQuery switch
            {
                var p when p.Contains("/branches/") => StubHttpHandler.Json(HttpStatusCode.OK, "{}"),
                var p when p.Contains("/pulls?state=open") => StubHttpHandler.Json(HttpStatusCode.OK, "[" + OPEN_PULL_REQUEST + "]"),
                _ => throw new InvalidOperationException("No debe crear otro pull request."),
            });

        Assert.True(result.Success);
        Assert.Contains("Se reutilizó el pull request abierto", result.ResultText);
        Assert.Equal(7, task.PullRequestNumber);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post);
    }

    // ------------------------------------------------------------------ merge

    [Fact]
    public async Task MergePullRequest_WithoutPullRequest_Fails()
    {
        var (result, handler, _) = await ExecuteAsync(
            Action: SystemAction.MERGE_PULL_REQUEST,
            Respond: _ => throw new InvalidOperationException());

        Assert.False(result.Success);
        Assert.Contains("no tiene pull request", result.ErrorMessage);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task MergePullRequest_AlreadyMerged_OnlyRecordsIt()
    {
        var (result, handler, task) = await ExecuteAsync(
            Action: SystemAction.MERGE_PULL_REQUEST,
            Respond: _ => StubHttpHandler.Json(HttpStatusCode.OK, MERGED_PULL_REQUEST),
            PullRequestNumber: 7);

        Assert.True(result.Success);
        Assert.Contains("ya estaba integrado", result.ResultText);
        Assert.NotNull(task.PullRequestMergedAt);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Put);
    }

    [Fact]
    public async Task MergePullRequest_NotMerged_MergesWithSquashAndRecordsIt()
    {
        var (result, handler, task) = await ExecuteAsync(
            Action: SystemAction.MERGE_PULL_REQUEST,
            Respond: request => request.Method == HttpMethod.Put
                ? StubHttpHandler.Json(HttpStatusCode.OK, """{"merged":true}""")
                : StubHttpHandler.Json(HttpStatusCode.OK, OPEN_PULL_REQUEST),
            PullRequestNumber: 7);

        Assert.True(result.Success);
        Assert.Contains("Merge (squash)", result.ResultText);
        Assert.NotNull(task.PullRequestMergedAt);
        var put = Assert.Single(handler.Requests, r => r.Method == HttpMethod.Put);
        Assert.Equal("/repos/acme/fabric/pulls/7/merge", put.PathAndQuery);
        Assert.Equal("#1 Mi tarea", JsonDocument.Parse(put.Body).RootElement.GetProperty("commit_title").GetString());
    }

    [Fact]
    public async Task MergePullRequest_WhenGitHubRejectsTheMerge_ThrowsSoTheOrchestratorCanFailTheRun()
    {
        await Assert.ThrowsAsync<GitHubException>(() => ExecuteAsync(
            Action: SystemAction.MERGE_PULL_REQUEST,
            Respond: request => request.Method == HttpMethod.Put
                ? StubHttpHandler.Json(HttpStatusCode.MethodNotAllowed, """{"message":"Pull Request is not mergeable"}""")
                : StubHttpHandler.Json(HttpStatusCode.OK, OPEN_PULL_REQUEST),
            PullRequestNumber: 7));
    }

    // ------------------------------------------------------------------ otros

    [Fact]
    public async Task StageWithoutAction_AdvancesWithoutCallingGitHub()
    {
        var (result, handler, _) = await ExecuteAsync(
            Action: SystemAction.NONE,
            Respond: _ => throw new InvalidOperationException());

        Assert.True(result.Success);
        Assert.Contains("sin acción configurada", result.ResultText);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Execute_WhenTheStageNoLongerExists_Throws()
    {
        var run = await SeedRunAsync(SystemAction.NONE);
        await using (var db = CreateDbContext())
        {
            db.Stages.Remove(await db.Stages.SingleAsync(s => s.ID == 2));
            await db.SaveChangesAsync();
        }
        await using var check = CreateDbContext();

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateExecutor(check, new StubHttpHandler(_ => throw new InvalidOperationException()))
            .ExecuteAsync(
                Run: run,
                CT: CancellationToken.None));
    }

    [Fact]
    public void Failure_BuildsAFailedCompletionWithTheMessage()
    {
        var completion = SystemActionExecutor.Failure("falló");

        Assert.False(completion.Success);
        Assert.False(completion.Cancelled);
        Assert.Equal("falló", completion.ErrorMessage);
        Assert.Empty(completion.ChangedFiles);
    }
}
