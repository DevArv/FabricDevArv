using System.Runtime.InteropServices;
using DevArv.Contracts.Models;
using Microsoft.Extensions.Options;
using DevArv.Worker.EnvironmentConfig;
using DevArv.Worker.Models;
using DevArv.Worker.Helpers;

namespace DevArv.Worker.Services;

/// <summary>Servicio principal: envía latidos al servidor y toma trabajos de agente para ejecutarlos.</summary>
public sealed class AgentWorkerService(
    ServerClient Server,
    JobRunner Runner,
    EnvironmentData EnvironmentData,
    IOptions<WorkerOptions> Options,
    ILogger<AgentWorkerService> Logger) : BackgroundService
{
    private readonly WorkerOptions _Options = Options.Value;
    private int _Running;

    protected override async Task ExecuteAsync(CancellationToken StoppingToken)
    {
        Logger.LogInformation("DevArv.Worker en {Machine} · servidor {Url} · espacio de trabajo {Root} · {Jobs} trabajo(s) en paralelo",
            Environment.MachineName, EnvironmentData.Url, _Options.Workspace.ResolveRoot(), _Options.MaxParallelJobs);
        await CheckToolsAsync(StoppingToken);
        Logger.LogInformation("Conexión y credenciales tomadas del appsettings (EnvironmentData, Worker:Claude, Worker:Git).");

        var loops = Enumerable.Range(0, Math.Max(1, _Options.MaxParallelJobs))
            .Select(i => JobLoopAsync(i, StoppingToken))
            .Append(HeartbeatLoopAsync(StoppingToken))
            .ToList();
        await Task.WhenAll(loops);
    }

    private async Task HeartbeatLoopAsync(CancellationToken CT)
    {
        var version = typeof(AgentWorkerService).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
        while (!CT.IsCancellationRequested)
        {
            try
            {
                var response = await Server.HeartbeatAsync(
                    new HeartbeatRequest(Environment.MachineName, version, RuntimeInformation.OSDescription, Volatile.Read(ref _Running)), CT);
                Logger.LogDebug("Latido OK como «{Name}»", response?.WorkerName);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger.LogWarning("No se pudo contactar al servidor: {Message}", ex.Message);
            }
            await Delay(TimeSpan.FromSeconds(30), CT);
        }
    }

    private async Task JobLoopAsync(int Slot, CancellationToken CT)
    {
        // Desfase inicial para que varios slots no consulten al mismo tiempo.
        await Delay(TimeSpan.FromMilliseconds(700 * Slot), CT);
        var backoff = TimeSpan.FromSeconds(Math.Max(2, _Options.PollSeconds));

        while (!CT.IsCancellationRequested)
        {
            AgentJob? job;
            try
            {
                job = await Server.ClaimAsync(CT);
                backoff = TimeSpan.FromSeconds(Math.Max(2, _Options.PollSeconds));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger.LogWarning("Error pidiendo trabajo: {Message}", ex.Message);
                await Delay(backoff, CT);
                backoff = TimeSpan.FromSeconds(Math.Min(60, backoff.TotalSeconds * 2));
                continue;
            }

            if (job is null)
            {
                await Delay(TimeSpan.FromSeconds(Math.Max(2, _Options.PollSeconds)), CT);
                continue;
            }

            Interlocked.Increment(ref _Running);
            try
            {
                await Runner.RunAsync(job, CT);
            }
            finally
            {
                Interlocked.Decrement(ref _Running);
            }
        }
    }

    private async Task CheckToolsAsync(CancellationToken CT)
    {
        var claude = ExecutableResolver.Resolve(_Options.Claude.Executable);
        await CheckAsync(
            Name: "Claude Code",
            File: claude.FileName,
            Args: [.. claude.PrefixArgs, "--version"],
            CT: CT);
        await CheckAsync(
            Name: "git",
            File: _Options.Git.Executable,
            Args: ["--version"],
            CT: CT);
    }

    private async Task CheckAsync(string Name, string File, IReadOnlyList<string> Args, CancellationToken CT)
    {
        try
        {
            var r = await ProcessRunner.RunAsync(new ProcessSpec(File, Args, Environment.CurrentDirectory), CT);
            if (r.Ok) Logger.LogInformation("{Name}: {Version}", Name, r.StdOut.Trim());
            else Logger.LogError("{Name} respondió con código {Code}: {Error}", Name, r.ExitCode, r.StdErr.Trim());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogError("No se encontró {Name} ({File}): {Message}. Instálalo y verifica el PATH o la configuración.", Name, File, ex.Message);
        }
    }

    private static async Task Delay(TimeSpan Delay, CancellationToken CT)
    {
        try { await Task.Delay(Delay, CT); }
        catch (OperationCanceledException) { }
    }
}
