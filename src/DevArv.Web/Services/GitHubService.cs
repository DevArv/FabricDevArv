using System.Net.Http.Headers;
using System.Net;
using System.Text.Json;
using DevArv.Web.Models;
using Microsoft.Extensions.Options;

namespace DevArv.Web.Services;

/// <summary>Cliente mínimo de la API REST de GitHub (repos, ramas y pull requests).</summary>
public sealed class GitHubService
{
    private readonly HttpClient _Http;
    private readonly GitHubOptions _Options;

    public GitHubService(HttpClient Http, IOptions<GitHubOptions> OptionsAccessor)
    {
        _Http = Http;
        _Options = OptionsAccessor.Value;
        _Http.BaseAddress = new Uri(_Options.ApiBaseUrl.TrimEnd('/') + "/");
        _Http.DefaultRequestHeaders.UserAgent.ParseAdd("DevArv/0.1");
        _Http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        _Http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        if (!string.IsNullOrWhiteSpace(_Options.Token))
            _Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _Options.Token.Trim());
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_Options.Token);

    public string CloneUrl(string Owner, string Repo) => $"{_Options.WebBaseUrl.TrimEnd('/')}/{Owner}/{Repo}.git";

    public async Task<RepoDetails> GetRepositoryAsync(string Owner, string Repo, CancellationToken CT)
    {
        using var doc = await SendAsync(
            Method: HttpMethod.Get,
            Url: $"repos/{Escape(Owner)}/{Escape(Repo)}",
            Payload: null,
            CT: CT);
        var root = doc.RootElement;
        return new RepoDetails(
            root.GetProperty("full_name").GetString()!,
            root.GetProperty("default_branch").GetString()!,
            root.GetProperty("private").GetBoolean(),
            root.GetProperty("html_url").GetString()!);
    }

    public async Task<IReadOnlyList<string>> ListBranchesAsync(string Owner, string Repo, CancellationToken CT)
    {
        using var doc = await SendAsync(
            Method: HttpMethod.Get,
            Url: $"repos/{Escape(Owner)}/{Escape(Repo)}/branches?per_page=100",
            Payload: null,
            CT: CT);
        return doc.RootElement.EnumerateArray().Select(b => b.GetProperty("name").GetString()!).ToList();
    }

    public async Task<bool> BranchExistsAsync(string Owner, string Repo, string Branch, CancellationToken CT)
    {
        try
        {
            var path = string.Join("/", Branch.Split('/').Select(Uri.EscapeDataString));
            using var _ = await SendAsync(
                Method: HttpMethod.Get,
                Url: $"repos/{Escape(Owner)}/{Escape(Repo)}/branches/{path}",
                Payload: null,
                CT: CT);
            return true;
        }
        catch (GitHubException ex) when (ex.Status == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task<PullRequestInfo> CreatePullRequestAsync(
        string Owner, string Repo, string Head, string Base, string Title, string Body, CancellationToken CT)
    {
        var payload = new { title = Title, head = Head, @base = Base, body = Body, maintainer_can_modify = true };
        using var doc = await SendAsync(
            Method: HttpMethod.Post,
            Url: $"repos/{Escape(Owner)}/{Escape(Repo)}/pulls",
            Payload: payload,
            CT: CT);
        return ToPullRequest(doc.RootElement);
    }

    public async Task<PullRequestInfo?> FindOpenPullRequestAsync(string Owner, string Repo, string Head, CancellationToken CT)
    {
        var query = $"repos/{Escape(Owner)}/{Escape(Repo)}/pulls?state=open&head={Escape(Owner + ":" + Head)}";
        using var doc = await SendAsync(
            Method: HttpMethod.Get,
            Url: query,
            Payload: null,
            CT: CT);
        var first = doc.RootElement.EnumerateArray().FirstOrDefault();
        return first.ValueKind == JsonValueKind.Object ? ToPullRequest(first) : null;
    }

    public async Task<PullRequestInfo> GetPullRequestAsync(string Owner, string Repo, int Number, CancellationToken CT)
    {
        using var doc = await SendAsync(
            Method: HttpMethod.Get,
            Url: $"repos/{Escape(Owner)}/{Escape(Repo)}/pulls/{Number}",
            Payload: null,
            CT: CT);
        return ToPullRequest(doc.RootElement);
    }

    public async Task MergePullRequestAsync(string Owner, string Repo, int Number, string CommitTitle, CancellationToken CT)
    {
        var payload = new { merge_method = "squash", commit_title = CommitTitle };
        using var _ = await SendAsync(
            Method: HttpMethod.Put,
            Url: $"repos/{Escape(Owner)}/{Escape(Repo)}/pulls/{Number}/merge",
            Payload: payload,
            CT: CT);
    }

    private static PullRequestInfo ToPullRequest(JsonElement PullRequest) => new(
        PullRequest.GetProperty("number").GetInt32(),
        PullRequest.GetProperty("html_url").GetString()!,
        PullRequest.GetProperty("state").GetString()!,
        PullRequest.TryGetProperty("merged", out var merged) && merged.ValueKind == JsonValueKind.True);

    private async Task<JsonDocument> SendAsync(HttpMethod Method, string Url, object? Payload, CancellationToken CT)
    {
        if (!IsConfigured)
            throw new GitHubException(HttpStatusCode.Unauthorized,
                "Falta configurar GitHub:Token en el appsettings del servidor (appsettings.Local.json).");

        using var request = new HttpRequestMessage(Method, Url);
        if (Payload is not null) request.Content = JsonContent.Create(Payload);
        using var response = await _Http.SendAsync(request, CT);
        var body = await response.Content.ReadAsStringAsync(CT);

        if (!response.IsSuccessStatusCode)
            throw new GitHubException(response.StatusCode, $"GitHub {(int)response.StatusCode}: {ExtractError(body)}");

        return JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
    }

    private static string ExtractError(string Body)
    {
        try
        {
            using var doc = JsonDocument.Parse(Body);
            var root = doc.RootElement;
            var message = root.TryGetProperty("message", out var m) ? m.GetString() : null;
            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
            {
                var details = errors.EnumerateArray()
                    .Select(e => e.ValueKind == JsonValueKind.Object && e.TryGetProperty("message", out var em)
                        ? em.GetString()
                        : e.ToString())
                    .Where(s => !string.IsNullOrWhiteSpace(s));
                message = $"{message} ({string.Join("; ", details)})";
            }
            return message ?? Body;
        }
        catch (JsonException)
        {
            return Body.Length > 300 ? Body[..300] : Body;
        }
    }

    private static string Escape(string Value) => Uri.EscapeDataString(Value);
}
