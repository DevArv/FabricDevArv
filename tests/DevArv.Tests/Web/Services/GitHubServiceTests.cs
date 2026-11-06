using System.Net;
using System.Text.Json;
using DevArv.Tests.TestSupport;
using DevArv.Web.Models;
using DevArv.Web.Services;
using Microsoft.Extensions.Options;

namespace DevArv.Tests.Web.Services;

public class GitHubServiceTests
{
    private const string PULL_REQUEST_JSON = """{"number":7,"html_url":"https://github.com/acme/fabric/pull/7","state":"open","merged":false}""";

    private static (GitHubService Service, StubHttpHandler Handler) Create(
        Func<CapturedRequest, HttpResponseMessage> Respond, string Token = "token")
    {
        var handler = new StubHttpHandler(Respond);
        var service = new GitHubService(new HttpClient(handler), Options.Create(new GitHubOptions { Token = Token }));
        return (service, handler);
    }

    [Fact]
    public async Task CreatePullRequestAsync_SendsTheExpectedPayload()
    {
        var (service, handler) = Create(_ => StubHttpHandler.Json(HttpStatusCode.Created, PULL_REQUEST_JSON));

        var pr = await service.CreatePullRequestAsync(
            Owner: "acme",
            Repo: "fabric",
            Head: "devarv/1-x",
            Base: "main",
            Title: "devarv-1-x",
            Body: "Descripción",
            CT: CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/repos/acme/fabric/pulls", request.PathAndQuery);
        using var json = JsonDocument.Parse(request.Body);
        Assert.Equal("devarv-1-x", json.RootElement.GetProperty("title").GetString());
        Assert.Equal("devarv/1-x", json.RootElement.GetProperty("head").GetString());
        Assert.Equal("main", json.RootElement.GetProperty("base").GetString());
        Assert.Equal("Descripción", json.RootElement.GetProperty("body").GetString());
        Assert.True(json.RootElement.GetProperty("maintainer_can_modify").GetBoolean());
        Assert.Equal(7, pr.Number);
        Assert.Equal("https://github.com/acme/fabric/pull/7", pr.HtmlUrl);
        Assert.False(pr.Merged);
    }

    [Fact]
    public async Task MergePullRequestAsync_UsesSquashWithTheGivenTitle()
    {
        var (service, handler) = Create(_ => StubHttpHandler.Json(HttpStatusCode.OK, """{"merged":true}"""));

        await service.MergePullRequestAsync(
            Owner: "acme",
            Repo: "fabric",
            Number: 7,
            CommitTitle: "#1 Mi tarea",
            CT: CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("/repos/acme/fabric/pulls/7/merge", request.PathAndQuery);
        using var json = JsonDocument.Parse(request.Body);
        Assert.Equal("squash", json.RootElement.GetProperty("merge_method").GetString());
        Assert.Equal("#1 Mi tarea", json.RootElement.GetProperty("commit_title").GetString());
    }

    [Fact]
    public async Task FindOpenPullRequestAsync_QueriesByOwnerAndBranch_AndReturnsTheFirstOne()
    {
        var (service, handler) = Create(_ => StubHttpHandler.Json(HttpStatusCode.OK, "[" + PULL_REQUEST_JSON + "]"));

        var pr = await service.FindOpenPullRequestAsync(
            Owner: "acme",
            Repo: "fabric",
            Head: "devarv/1-x",
            CT: CancellationToken.None);

        Assert.Equal(7, pr!.Number);
        Assert.Equal("/repos/acme/fabric/pulls?state=open&head=acme%3Adevarv%2F1-x", handler.Requests.Single().PathAndQuery);
    }

    [Fact]
    public async Task FindOpenPullRequestAsync_WithoutResults_ReturnsNull()
    {
        var (service, _) = Create(_ => StubHttpHandler.Json(HttpStatusCode.OK, "[]"));

        var pr = await service.FindOpenPullRequestAsync(
            Owner: "acme",
            Repo: "fabric",
            Head: "devarv/1-x",
            CT: CancellationToken.None);

        Assert.Null(pr);
    }

    [Fact]
    public async Task GetPullRequestAsync_ParsesTheMergedFlag_AndTreatsItAsFalseWhenMissing()
    {
        var (merged, _) = Create(_ => StubHttpHandler.Json(HttpStatusCode.OK,
            """{"number":7,"html_url":"u","state":"closed","merged":true}"""));
        var (withoutFlag, _) = Create(_ => StubHttpHandler.Json(HttpStatusCode.OK,
            """{"number":7,"html_url":"u","state":"open"}"""));

        var a = await merged.GetPullRequestAsync(
            Owner: "acme",
            Repo: "fabric",
            Number: 7,
            CT: CancellationToken.None);
        var b = await withoutFlag.GetPullRequestAsync(
            Owner: "acme",
            Repo: "fabric",
            Number: 7,
            CT: CancellationToken.None);

        Assert.True(a.Merged);
        Assert.Equal("closed", a.State);
        Assert.False(b.Merged);
    }

    [Fact]
    public async Task BranchExistsAsync_EscapesEachSegment_AndReportsFoundAndNotFound()
    {
        var (found, handler) = Create(_ => StubHttpHandler.Json(HttpStatusCode.OK, "{}"));
        var (missing, _) = Create(_ => StubHttpHandler.Json(HttpStatusCode.NotFound, """{"message":"Branch not found"}"""));

        Assert.True(await found.BranchExistsAsync(
            Owner: "acme",
            Repo: "fabric",
            Branch: "devarv/mi rama",
            CT: CancellationToken.None));
        Assert.False(await missing.BranchExistsAsync(
            Owner: "acme",
            Repo: "fabric",
            Branch: "devarv/x",
            CT: CancellationToken.None));
        Assert.Equal("/repos/acme/fabric/branches/devarv/mi%20rama", handler.Requests.Single().PathAndQuery);
    }

    [Fact]
    public async Task BranchExistsAsync_OtherErrors_AreNotSwallowed()
    {
        var (service, _) = Create(_ => StubHttpHandler.Json(HttpStatusCode.InternalServerError, """{"message":"boom"}"""));

        var ex = await Assert.ThrowsAsync<GitHubException>(() => service.BranchExistsAsync(
            Owner: "acme",
            Repo: "fabric",
            Branch: "x",
            CT: CancellationToken.None));

        Assert.Equal(HttpStatusCode.InternalServerError, ex.Status);
    }

    [Fact]
    public async Task Errors_IncludeGitHubsMessageAndItsDetails()
    {
        var (service, _) = Create(_ => StubHttpHandler.Json(HttpStatusCode.UnprocessableEntity,
            """{"message":"Validation Failed","errors":[{"message":"A pull request already exists"},"otro"]}"""));

        var ex = await Assert.ThrowsAsync<GitHubException>(() => service.CreatePullRequestAsync(
            Owner: "acme",
            Repo: "fabric",
            Head: "h",
            Base: "main",
            Title: "t",
            Body: "b",
            CT: CancellationToken.None));

        Assert.Contains("422", ex.Message);
        Assert.Contains("Validation Failed", ex.Message);
        Assert.Contains("A pull request already exists", ex.Message);
        Assert.Contains("otro", ex.Message);
    }

    [Fact]
    public async Task Errors_WithABodyThatIsNotJson_ShowTheTruncatedText()
    {
        var (service, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent(new string('x', 500)) });

        var ex = await Assert.ThrowsAsync<GitHubException>(() => service.GetPullRequestAsync(
            Owner: "acme",
            Repo: "fabric",
            Number: 1,
            CT: CancellationToken.None));

        Assert.Contains("502", ex.Message);
        Assert.True(ex.Message.Length < 400);
    }

    [Fact]
    public async Task WithoutToken_FailsBeforeCallingGitHub()
    {
        var (service, handler) = Create(_ => StubHttpHandler.Json(HttpStatusCode.OK, "{}"), Token: " ");

        var ex = await Assert.ThrowsAsync<GitHubException>(() => service.GetPullRequestAsync(
            Owner: "acme",
            Repo: "fabric",
            Number: 1,
            CT: CancellationToken.None));

        Assert.False(service.IsConfigured);
        Assert.Equal(HttpStatusCode.Unauthorized, ex.Status);
        Assert.Contains("GitHub:Token", ex.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void CloneUrl_UsesTheConfiguredWebBase()
    {
        var service = new GitHubService(new HttpClient(), Options.Create(new GitHubOptions { WebBaseUrl = "https://git.example.com/", Token = "t" }));

        Assert.Equal("https://git.example.com/acme/fabric.git", service.CloneUrl("acme", "fabric"));
    }

    [Fact]
    public async Task GetRepositoryAsync_And_ListBranchesAsync_MapTheResponse()
    {
        var (service, _) = Create(request => request.PathAndQuery.Contains("/branches")
            ? StubHttpHandler.Json(HttpStatusCode.OK, """[{"name":"main"},{"name":"dev"}]""")
            : StubHttpHandler.Json(HttpStatusCode.OK, """{"full_name":"acme/fabric","default_branch":"main","private":true,"html_url":"h"}"""));

        var details = await service.GetRepositoryAsync(
            Owner: "acme",
            Repo: "fabric",
            CT: CancellationToken.None);
        var branches = await service.ListBranchesAsync(
            Owner: "acme",
            Repo: "fabric",
            CT: CancellationToken.None);

        Assert.Equal("acme/fabric", details.FullName);
        Assert.True(details.IsPrivate);
        Assert.Equal(["main", "dev"], branches);
    }
}
