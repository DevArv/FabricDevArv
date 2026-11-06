using System.Net;
using System.Net.Http.Json;
using DevArv.Contracts.Models;
using DevArv.Worker.EnvironmentConfig;

namespace DevArv.Worker.Services;

/// <summary>Cliente HTTP de la API /api/worker del servidor DevArv.</summary>
public sealed class ServerClient
{
    private readonly HttpClient _Http;

    public ServerClient(HttpClient Http, EnvironmentData EnvironmentData)
    {
        _Http = Http;
        _Http.BaseAddress = EnvironmentData.GetBaseAddress();
        _Http.DefaultRequestHeaders.Remove(WorkerHeaders.ApiKey);
        _Http.DefaultRequestHeaders.Add(WorkerHeaders.ApiKey, EnvironmentData.ApiKey);
        _Http.Timeout = TimeSpan.FromSeconds(100);
    }

    public async Task<HeartbeatResponse?> HeartbeatAsync(HeartbeatRequest Request, CancellationToken CT)
    {
        using var response = await _Http.PostAsJsonAsync("api/worker/heartbeat", Request, CT);
        await EnsureSuccessAsync(response, CT);
        return await response.Content.ReadFromJsonAsync<HeartbeatResponse>(CT);
    }

    public async Task<AgentJob?> ClaimAsync(CancellationToken CT)
    {
        using var response = await _Http.PostAsJsonAsync("api/worker/claim", new ClaimRequest(Environment.MachineName), CT);
        if (response.StatusCode == HttpStatusCode.NoContent) return null;
        await EnsureSuccessAsync(response, CT);
        return await response.Content.ReadFromJsonAsync<AgentJob>(CT);
    }

    public async Task<LogChunkResponse> AppendLogAsync(long RunID, string Text, CancellationToken CT)
    {
        using var response = await _Http.PostAsJsonAsync($"api/worker/runs/{RunID}/log", new LogChunk(Text), CT);
        if (response.StatusCode == HttpStatusCode.NotFound) return new LogChunkResponse(CancelRequested: true);
        await EnsureSuccessAsync(response, CT);
        return await response.Content.ReadFromJsonAsync<LogChunkResponse>(CT) ?? new LogChunkResponse(false);
    }

    public async Task CompleteAsync(long RunID, RunCompletion Completion, CancellationToken CT)
    {
        using var response = await _Http.PostAsJsonAsync($"api/worker/runs/{RunID}/complete", Completion, CT);
        await EnsureSuccessAsync(response, CT);
    }

    public async Task DownloadAttachmentAsync(long RunID, long AttachmentID, string TargetPath, CancellationToken CT)
    {
        using var response = await _Http.GetAsync($"api/worker/runs/{RunID}/attachments/{AttachmentID}",
            HttpCompletionOption.ResponseHeadersRead, CT);
        await EnsureSuccessAsync(response, CT);
        await using var target = File.Create(TargetPath);
        await response.Content.CopyToAsync(target, CT);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage Response, CancellationToken CT)
    {
        if (Response.IsSuccessStatusCode) return;
        var body = await Response.Content.ReadAsStringAsync(CT);
        if (body.Length > 500) body = body[..500];
        var hint = Response.StatusCode == HttpStatusCode.Unauthorized
            ? " (revisa EnvironmentData:ApiKey y que la PC esté habilitada en «Mis PCs»)"
            : "";
        throw new HttpRequestException($"Servidor respondió {(int)Response.StatusCode}{hint}: {body}", null, Response.StatusCode);
    }
}
