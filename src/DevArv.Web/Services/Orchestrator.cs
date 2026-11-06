using DevArv.Contracts.Models;

namespace DevArv.Web.Services;

/// <summary>
/// Proceso en segundo plano del servidor:
/// 1) ejecuta las columnas de sistema (crear PR, merge) y
/// 2) marca como fallidas las ejecuciones de workers que dejaron de responder o excedieron su tiempo y
/// 3) mueve a la columna destino del merge las tareas cuyo pull request se integró en GitHub.
/// </summary>
public sealed class Orchestrator(IServiceScopeFactory Scopes, ILogger<Orchestrator> Logger) : BackgroundService
{
    private static readonly TimeSpan _Interval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan _WatchdogInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan _WorkerSilence = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan _MergeCheckInterval = TimeSpan.FromMinutes(1);
    private const int MAX_SYSTEM_RUNS_PER_CYCLE = 10;

    private DateTimeOffset _LastWatchdog = DateTimeOffset.MinValue;
    private DateTimeOffset _LastMergeCheck = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken StoppingToken)
    {
        // Pequeña espera para que la base de datos termine de inicializarse.
        await Task.Delay(TimeSpan.FromSeconds(3), StoppingToken);

        using (var scope = Scopes.CreateScope())
        {
            if (!scope.ServiceProvider.GetRequiredService<GitHubService>().IsConfigured)
                Logger.LogWarning("GitHub:Token no está configurado: no se podrán conectar repositorios ni abrir pull requests. " +
                    "Defínelo en el appsettings del servidor (appsettings.Local.json).");
        }

        while (!StoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessSystemRunsAsync(StoppingToken);
                if (DateTimeOffset.UtcNow - _LastWatchdog > _WatchdogInterval)
                {
                    await WatchdogAsync(StoppingToken);
                    _LastWatchdog = DateTimeOffset.UtcNow;
                }
                if (DateTimeOffset.UtcNow - _LastMergeCheck > _MergeCheckInterval)
                {
                    _LastMergeCheck = DateTimeOffset.UtcNow;
                    await MoveMergedPullRequestsAsync(StoppingToken);
                }
            }
            catch (OperationCanceledException) when (StoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error en el ciclo del orquestador");
            }

            await Task.Delay(_Interval, StoppingToken);
        }
    }

    private async Task ProcessSystemRunsAsync(CancellationToken CT)
    {
        for (var i = 0; i < MAX_SYSTEM_RUNS_PER_CYCLE; i++)
        {
            using var scope = Scopes.CreateScope();
            var workflow = scope.ServiceProvider.GetRequiredService<WorkflowService>();
            var executor = scope.ServiceProvider.GetRequiredService<SystemActionExecutor>();

            var run = await workflow.ClaimNextSystemRunAsync(CT);
            if (run is null) return;

            RunCompletion completion;
            try
            {
                completion = await executor.ExecuteAsync(run, CT);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger.LogWarning(ex, "Falló la acción de sistema de la ejecución {RunId}", run.ID);
                completion = SystemActionExecutor.Failure(ex.Message);
            }
            await workflow.CompleteRunAsync(
                RunID: run.ID,
                Completion: completion,
                Actor: "Sistema",
                CT: CT);
        }
    }

    /// <summary>
    /// Consulta en GitHub los pull requests abiertos de las tareas y, si alguno se integró (merge),
    /// hace entrar la tarea a la columna marcada como destino del merge (por defecto «Pendiente Deploy»).
    /// </summary>
    private async Task MoveMergedPullRequestsAsync(CancellationToken CT)
    {
        using var scope = Scopes.CreateScope();
        var github = scope.ServiceProvider.GetRequiredService<GitHubService>();
        if (!github.IsConfigured) return;
        var workflow = scope.ServiceProvider.GetRequiredService<WorkflowService>();

        foreach (var watched in await workflow.GetPullRequestsToWatchAsync(CT))
        {
            try
            {
                var pr = await github.GetPullRequestAsync(
                    Owner: watched.RepoOwner,
                    Repo: watched.RepoName,
                    Number: watched.Number,
                    CT: CT);
                if (!pr.Merged) continue;
                Logger.LogInformation("Pull request #{Number} de la tarea {TaskId} integrado; se mueve la tarea.",
                    watched.Number, watched.TaskID);
                await workflow.OnPullRequestMergedAsync(
                    TaskID: watched.TaskID,
                    Number: watched.Number,
                    Actor: "Sistema",
                    CT: CT);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger.LogWarning(ex, "No se pudo revisar el pull request #{Number} de la tarea {TaskId}",
                    watched.Number, watched.TaskID);
            }
        }
    }

    private async Task WatchdogAsync(CancellationToken CT)
    {
        using var scope = Scopes.CreateScope();
        var workflow = scope.ServiceProvider.GetRequiredService<WorkflowService>();
        await workflow.FailStalledRunsAsync(
            WorkerSilence: _WorkerSilence,
            CT: CT);
    }
}
