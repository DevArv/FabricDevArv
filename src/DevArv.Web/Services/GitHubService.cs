using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace DevArv.Web.Services;

public sealed class GitHubOptions
{
    /// <summary>Token (fine-grained o classic) con permiso de lectura de contenido y escritura de pull requests.</summary>
    public string Token { get; set; } = "";
    public string ApiBaseUrl { get; set; } = "https://api.github.com/";
    public string WebBaseUrl { get; set; } = "https://github.com";
}

public sealed record RepoDetails(string FullName, string DefaultBranch, bool IsPrivate, string HtmlUrl);

public sealed record PullRequestInfo(int Number, string HtmlUrl, string State, bool Merged);

public sealed class GitHubException(HttpStatusCode status, string message) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
}

/// <summary>Cliente mínimo de la API REST de GitHub (repos, ramas y pull requests).</summary>
public sealed class GitHubService
{
    private readonly HttpClient _http;
    private readonly GitHubOptions _options;

    public GitHubService(HttpClient http, IOptions<GitHubOptions> options)
    {
        _http = http;
        _options = options.Value;
        _http.BaseAddress = new Uri(_options.ApiBaseUrl.TrimEnd('/') + "/");
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("DevArv/0.1");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        if (!string.IsNullOrWhiteSpace(_options.Token))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.Token.Trim());
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.Token);

    public string CloneUrl(string owner, string repo) => $"{_options.WebBaseUrl.TrimEnd('/')}/{owner}/{repo}.git";

    public async Task<RepoDetails> GetRepositoryAsync(string owner, string repo, CancellationToken ct)
    {
        using var doc = await SendAsync(HttpMethod.Get, $"repos/{E(owner)}/{E(repo)}", null, ct);
        var r = doc.RootElement;
        return new RepoDetails(
            r.GetProperty("full_name").GetString()!,
            r.GetProperty("default_branch").GetString()!,
            r.GetProperty("private").GetBoolean(),
            r.GetProperty("html_url").GetString()!);
    }

    public async Task<IReadOnlyList<string>> ListBranchesAsync(string owner, string repo, CancellationToken ct)
    {
        using var doc = await SendAsync(HttpMethod.Get, $"repos/{E(owner)}/{E(repo)}/branches?per_page=100", null, ct);
        return doc.RootElement.EnumerateArray().Select(b => b.GetProperty("name").GetString()!).ToList();
    }

    public async Task<bool> BranchExistsAsync(string owner, string repo, string branch, CancellationToken ct)
    {
        try
        {
            var path = string.Join("/", branch.Split('/').Select(Uri.EscapeDataString));
            using var _ = await SendAsync(HttpMethod.Get, $"repos/{E(owner)}/{E(repo)}/branches/{path}", null, ct);
            return true;
        }
        catch (GitHubException ex) when (ex.Status == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task<PullRequestInfo> CreatePullRequestAsync(
        string owner, string repo, string head, string @base, string title, string body, CancellationToken ct)
    {
        var payload = new { title, head, @base, body, maintainer_can_modify = true };
        using var doc = await SendAsync(HttpMethod.Post, $"repos/{E(owner)}/{E(repo)}/pulls", payload, ct);
        return ToPullRequest(doc.RootElement);
    }

    public async Task<PullRequestInfo?> FindOpenPullRequestAsync(string owner, string repo, string head, CancellationToken ct)
    {
        var query = $"repos/{E(owner)}/{E(repo)}/pulls?state=open&head={E(owner + ":" + head)}";
        using var doc = await SendAsync(HttpMethod.Get, query, null, ct);
        var first = doc.RootElement.EnumerateArray().FirstOrDefault();
        return first.ValueKind == JsonValueKind.Object ? ToPullRequest(first) : null;
    }

    public async Task<PullRequestInfo> GetPullRequestAsync(string owner, string repo, int number, CancellationToken ct)
    {
        using var doc = await SendAsync(HttpMethod.Get, $"repos/{E(owner)}/{E(repo)}/pulls/{number}", null, ct);
        return ToPullRequest(doc.RootElement);
    }

    public async Task MergePullRequestAsync(string owner, string repo, int number, string commitTitle, CancellationToken ct)
    {
        var payload = new { merge_method = "squash", commit_title = commitTitle };
        using var _ = await SendAsync(HttpMethod.Put, $"repos/{E(owner)}/{E(repo)}/pulls/{number}/merge", payload, ct);
    }

    private static PullRequestInfo ToPullRequest(JsonElement pr) => new(
        pr.GetProperty("number").GetInt32(),
        pr.GetProperty("html_url").GetString()!,
        pr.GetProperty("state").GetString()!,
        pr.TryGetProperty("merged", out var merged) && merged.ValueKind == JsonValueKind.True);

    private async Task<JsonDocument> SendAsync(HttpMethod method, string url, object? payload, CancellationToken ct)
    {
        if (!IsConfigured)
            throw new GitHubException(HttpStatusCode.Unauthorized,
                "Falta configurar GitHub:Token en el servidor (user-secrets o variable de entorno GitHub__Token).");

        using var request = new HttpRequestMessage(method, url);
        if (payload is not null) request.Content = JsonContent.Create(payload);
        using var response = await _http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new GitHubException(response.StatusCode, $"GitHub {(int)response.StatusCode}: {ExtractError(body)}");

        return JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
    }

    private static string ExtractError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
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
            return message ?? body;
        }
        catch (JsonException)
        {
            return body.Length > 300 ? body[..300] : body;
        }
    }

    private static string E(string value) => Uri.EscapeDataString(value);
}
