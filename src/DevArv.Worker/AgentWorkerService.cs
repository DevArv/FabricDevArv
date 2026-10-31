using System.Runtime.InteropServices;
using DevArv.Contracts;
using DevArv.Worker.Agents;
using DevArv.Worker.Infrastructure;
using Microsoft.Extensions.Options;

namespace DevArv.Worker;

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
