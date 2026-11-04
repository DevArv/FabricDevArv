using DevArv.Contracts.Models;
using DevArv.Web.DatabaseConnection;
using DevArv.Web.Enums.Runs;
using DevArv.Web.Enums.Workflow;
using Microsoft.EntityFrameworkCore;

namespace DevArv.Web.Services;

/// <summary>
/// Proceso en segundo plano del servidor:
/// 1) ejecuta las columnas de sistema (crear PR, merge) y
/// 2) marca como fallidas las ejecuciones de workers que dejaron de responder o excedieron su tiempo.
/// </summary>
public sealed class Orchestrator(IServiceScopeFactory scopes, ILogger<Orchestrator> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan WorkerSilence = TimeSpan.FromMinutes(3);
    private DateTimeOffset _lastWatchdog = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Pequeña espera para que la base de datos termine de inicializarse.
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);

        using (var scope = scopes.CreateScope())
        {
            if (!scope.ServiceProvider.GetRequiredService<GitHubService>().IsConfigured)
                logger.LogWarning("GitHub:Token no está configurado: no se podrán conectar repositorios ni abrir pull requests. " +
                    "Defínelo en appsettings.Local.json, user-secrets o la variable de entorno GitHub__Token.");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessSystemRunsAsync(stoppingToken);
                if (DateTimeOffset.UtcNow - _lastWatchdog > TimeSpan.FromSeconds(30))
                {
                    await WatchdogAsync(stoppingToken);
                    _lastWatchdog = DateTimeOffset.UtcNow;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error en el ciclo del orquestador");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task ProcessSystemRunsAsync(CancellationToken ct)
    {
        for (var i = 0; i < 10; i++)
        {
            using var scope = scopes.CreateScope();
            var workflow = scope.ServiceProvider.GetRequiredService<WorkflowService>();
            var executor = scope.ServiceProvider.GetRequiredService<SystemActionExecutor>();

            var run = await workflow.ClaimNextSystemRunAsync(ct);
            if (run is null) return;

            RunCompletion completion;
            try
            {
                completion = await executor.ExecuteAsync(run, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Falló la acción de sistema de la ejecución {RunId}", run.Id);
                completion = SystemActionExecutor.Failure(ex.Message);
            }
            await workflow.CompleteRunAsync(run.Id, completion, "Sistema", ct);
        }
    }

    private async Task WatchdogAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var workflow = scope.ServiceProvider.GetRequiredService<WorkflowService>();
        var now = DateTimeOffset.UtcNow;

        var running = await db.TaskRuns
            .Where(r => r.Status == RunStatus.Running && r.RunnerKind == StageKind.Agent)
            .Select(r => new
            {
                r.Id,
                r.StartedAt,
                r.LastActivityAt,
                WorkerSeen = r.Worker != null ? r.Worker.LastSeenAt : null,
                Timeout = db.Stages.Where(s => s.Id == r.StageId).Select(s => (int?)s.TimeoutMinutes).FirstOrDefault(),
            })
            .ToListAsync(ct);

        foreach (var r in running)
        {
            string? reason = null;
            var silentSince = r.LastActivityAt ?? r.StartedAt ?? now;
            if (now - silentSince > WorkerSilence && (r.WorkerSeen is null || now - r.WorkerSeen > WorkerSilence))
                reason = "El worker dejó de responder durante la ejecución.";
            else if (r.StartedAt is { } started && now - started > TimeSpan.FromMinutes((r.Timeout ?? 60) + 15))
                reason = "La ejecución excedió el tiempo máximo de la columna.";

            if (reason is null) continue;
            logger.LogWarning("Ejecución {RunId}: {Reason}", r.Id, reason);
            await workflow.CompleteRunAsync(r.Id, SystemActionExecutor.Failure(reason), "Vigilante", ct);
        }
    }
}
