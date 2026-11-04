using System.Runtime.InteropServices;
using DevArv.Contracts.Models;
using Microsoft.Extensions.Options;
using DevArv.Worker.Models;
using DevArv.Worker.Helpers;

namespace DevArv.Worker.Services;

/// <summary>Servicio principal: envía latidos al servidor y toma trabajos de agente para ejecutarlos.</summary>
public sealed class AgentWorkerService(
    ServerClient server,
    JobRunner runner,
    IOptions<WorkerOptions> options,
    ILogger<AgentWorkerService> logger) : BackgroundService
{
    private readonly WorkerOptions _options = options.Value;
    private int _running;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_options.Server.Url) || string.IsNullOrWhiteSpace(_options.Server.ApiKey))
        {
            logger.LogCritical("Configura Server:Url y Server:ApiKey (clave generada en «Mis PCs»). El worker no tomará trabajos.");
            return;
        }

        logger.LogInformation("DevArv.Worker en {Machine} · servidor {Url} · espacio de trabajo {Root} · {Jobs} trabajo(s) en paralelo",
            Environment.MachineName, _options.Server.Url, _options.Workspace.ResolveRoot(), _options.MaxParallelJobs);
        await CheckToolsAsync(stoppingToken);
        LogCredentialSources();

        var loops = Enumerable.Range(0, Math.Max(1, _options.MaxParallelJobs))
            .Select(i => JobLoopAsync(i, stoppingToken))
            .Append(HeartbeatLoopAsync(stoppingToken))
            .ToList();
        await Task.WhenAll(loops);
    }

    private async Task HeartbeatLoopAsync(CancellationToken ct)
    {
        var version = typeof(AgentWorkerService).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var response = await server.HeartbeatAsync(
                    new HeartbeatRequest(Environment.MachineName, version, RuntimeInformation.OSDescription, Volatile.Read(ref _running)), ct);
                logger.LogDebug("Latido OK como «{Name}»", response?.WorkerName);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("No se pudo contactar al servidor: {Message}", ex.Message);
            }
            await Delay(TimeSpan.FromSeconds(30), ct);
        }
    }

    private async Task JobLoopAsync(int slot, CancellationToken ct)
    {
        // Desfase inicial para que varios slots no consulten al mismo tiempo.
        await Delay(TimeSpan.FromMilliseconds(700 * slot), ct);
        var backoff = TimeSpan.FromSeconds(Math.Max(2, _options.PollSeconds));

        while (!ct.IsCancellationRequested)
        {
            AgentJob? job;
            try
            {
                job = await server.ClaimAsync(ct);
                backoff = TimeSpan.FromSeconds(Math.Max(2, _options.PollSeconds));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("Error pidiendo trabajo: {Message}", ex.Message);
                await Delay(backoff, ct);
                backoff = TimeSpan.FromSeconds(Math.Min(60, backoff.TotalSeconds * 2));
                continue;
            }

            if (job is null)
            {
                await Delay(TimeSpan.FromSeconds(Math.Max(2, _options.PollSeconds)), ct);
                continue;
            }

            Interlocked.Increment(ref _running);
            try
            {
                await runner.RunAsync(job, ct);
            }
            finally
            {
                Interlocked.Decrement(ref _running);
            }
        }
    }

    private async Task CheckToolsAsync(CancellationToken ct)
    {
        var claude = ExecutableResolver.Resolve(_options.Claude.Executable);
        await CheckAsync("Claude Code", claude.FileName, [.. claude.PrefixArgs, "--version"], ct);
        await CheckAsync("git", _options.Git.Executable, ["--version"], ct);
    }

    /// <summary>Indica (sin mostrar los valores) de dónde saldrán las credenciales de Claude y de GitHub.</summary>
    private void LogCredentialSources()
    {
        var claudeEnv = _options.Claude.Environment;
        if (!string.IsNullOrWhiteSpace(_options.Claude.AnthropicApiKey))
            logger.LogInformation("Claude: se usará la API key de Worker:Claude:AnthropicApiKey.");
        else if (claudeEnv.TryGetValue("ANTHROPIC_API_KEY", out var k) && !string.IsNullOrWhiteSpace(k))
            logger.LogInformation("Claude: se usará ANTHROPIC_API_KEY de Worker:Claude:Environment.");
        else if (claudeEnv.TryGetValue("CLAUDE_CODE_OAUTH_TOKEN", out var t) && !string.IsNullOrWhiteSpace(t))
            logger.LogInformation("Claude: se usará el token de suscripción CLAUDE_CODE_OAUTH_TOKEN de Worker:Claude:Environment.");
        else if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")))
            logger.LogInformation("Claude: se usará la variable de entorno ANTHROPIC_API_KEY.");
        else if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CLAUDE_CODE_OAUTH_TOKEN")))
            logger.LogInformation("Claude: se usará la variable de entorno CLAUDE_CODE_OAUTH_TOKEN.");
        else
            logger.LogWarning("Claude: no hay API key configurada; se usará la sesión de `claude` iniciada por el usuario {User}. " +
                "Si el worker corre como servicio con otro usuario, define Worker:Claude:AnthropicApiKey.", Environment.UserName);

        if (!string.IsNullOrWhiteSpace(_options.Git.GitHubToken))
            logger.LogInformation("GitHub: se usará el token de Worker:Git:GitHubToken para clonar y hacer push.");
        else
            logger.LogWarning("GitHub: Worker:Git:GitHubToken está vacío; se usarán las credenciales de git de esta PC " +
                "(Git Credential Manager, SSH). Si no hay, el clon de repositorios privados fallará.");
    }

    private async Task CheckAsync(string name, string file, IReadOnlyList<string> args, CancellationToken ct)
    {
        try
        {
            var r = await ProcessRunner.RunAsync(new ProcessSpec(file, args, Environment.CurrentDirectory), ct);
            if (r.Ok) logger.LogInformation("{Name}: {Version}", name, r.StdOut.Trim());
            else logger.LogError("{Name} respondió con código {Code}: {Error}", name, r.ExitCode, r.StdErr.Trim());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError("No se encontró {Name} ({File}): {Message}. Instálalo y verifica el PATH o la configuración.", name, file, ex.Message);
        }
    }

    private static async Task Delay(TimeSpan delay, CancellationToken ct)
    {
        try { await Task.Delay(delay, ct); }
        catch (OperationCanceledException) { }
    }
}
