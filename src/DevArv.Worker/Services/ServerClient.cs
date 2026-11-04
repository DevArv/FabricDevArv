using System.Net;
using System.Net.Http.Json;
using DevArv.Contracts.Models;
using DevArv.Worker.EnvironmentConfig;

namespace DevArv.Worker.Services;

/// <summary>Cliente HTTP de la API /api/worker del servidor DevArv.</summary>
public sealed class ServerClient
{
    private readonly HttpClient _http;

    public ServerClient(HttpClient http, EnvironmentData environmentData)
    {
        _http = http;
        _http.BaseAddress = environmentData.GetBaseAddress();
        _http.DefaultRequestHeaders.Remove(WorkerHeaders.ApiKey);
        _http.DefaultRequestHeaders.Add(WorkerHeaders.ApiKey, environmentData.ApiKey);
        _http.Timeout = TimeSpan.FromSeconds(100);
    }

    public async Task<HeartbeatResponse?> HeartbeatAsync(HeartbeatRequest request, CancellationToken ct)
    {
        using var response = await _http.PostAsJsonAsync("api/worker/heartbeat", request, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<HeartbeatResponse>(ct);
    }

    public async Task<AgentJob?> ClaimAsync(CancellationToken ct)
    {
        using var response = await _http.PostAsJsonAsync("api/worker/claim", new ClaimRequest(Environment.MachineName), ct);
        if (response.StatusCode == HttpStatusCode.NoContent) return null;
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<AgentJob>(ct);
    }

    public async Task<LogChunkResponse> AppendLogAsync(long runId, string text, CancellationToken ct)
    {
        using var response = await _http.PostAsJsonAsync($"api/worker/runs/{runId}/log", new LogChunk(text), ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return new LogChunkResponse(CancelRequested: true);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<LogChunkResponse>(ct) ?? new LogChunkResponse(false);
    }

    public async Task CompleteAsync(long runId, RunCompletion completion, CancellationToken ct)
    {
        using var response = await _http.PostAsJsonAsync($"api/worker/runs/{runId}/complete", completion, ct);
        await EnsureSuccessAsync(response, ct);
    }

    public async Task DownloadAttachmentAsync(long runId, long attachmentId, string targetPath, CancellationToken ct)
    {
        using var response = await _http.GetAsync($"api/worker/runs/{runId}/attachments/{attachmentId}",
            HttpCompletionOption.ResponseHeadersRead, ct);
        await EnsureSuccessAsync(response, ct);
        await using var target = File.Create(targetPath);
        await response.Content.CopyToAsync(target, ct);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(ct);
        if (body.Length > 500) body = body[..500];
        var hint = response.StatusCode == HttpStatusCode.Unauthorized
            ? " (revisa EnvironmentData:ApiKey y que la PC esté habilitada en «Mis PCs»)"
            : "";
        throw new HttpRequestException($"Servidor respondió {(int)response.StatusCode}{hint}: {body}", null, response.StatusCode);
    }
}
