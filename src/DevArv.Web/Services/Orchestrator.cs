using DevArv.Contracts;
using DevArv.Web.Data;
using DevArv.Web.Domain;
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

/// <summary>Acciones automáticas del servidor sobre GitHub.</summary>
public sealed class SystemActionExecutor(AppDbContext db, GitHubService github, IConfiguration config)
{
    public static RunCompletion Failure(string message) =>
        new(false, false, message, null, 0, 0, 0, 0, 0, null, null, null, null, false, []);

    private static RunCompletion Success(string text) =>
        new(true, false, null, text, 0, 0, 0, 0, 0, null, null, null, null, false, []);

    public async Task<RunCompletion> ExecuteAsync(TaskRun run, CancellationToken ct)
    {
        var stage = await db.Stages.FindAsync([run.StageId], ct)
                    ?? throw new InvalidOperationException("La columna de la ejecución ya no existe.");
        var task = run.Task;
        return stage.SystemAction switch
        {
            SystemAction.CreatePullRequest => await CreatePullRequestAsync(task, ct),
            SystemAction.MergePullRequest => await MergePullRequestAsync(task, ct),
            _ => Success("Columna de sistema sin acción configurada; se avanza."),
        };
    }

    private async Task<RunCompletion> CreatePullRequestAsync(TaskItem task, CancellationToken ct)
    {
        var p = task.Project;
        if (string.IsNullOrEmpty(task.WorkBranch))
            return Failure("La tarea no tiene rama de trabajo; primero debe pasar por un agente desarrollador.");

        if (task.PullRequestNumber is { } existingNumber)
        {
            var existing = await github.GetPullRequestAsync(p.RepoOwner, p.RepoName, existingNumber, ct);
            if (existing.State == "open")
                return Success($"El pull request ya existe y sigue abierto: {existing.HtmlUrl}");
        }

        if (!await github.BranchExistsAsync(p.RepoOwner, p.RepoName, task.WorkBranch, ct))
            return Failure($"La rama {task.WorkBranch} no existe en GitHub. ¿El desarrollador hizo push?");

        var open = await github.FindOpenPullRequestAsync(p.RepoOwner, p.RepoName, task.WorkBranch, ct);
        var pr = open ?? await github.CreatePullRequestAsync(
            p.RepoOwner, p.RepoName, task.WorkBranch, task.BaseBranch,
            $"#{task.Id} {task.Title}", await BuildBodyAsync(task, ct), ct);

        task.PullRequestNumber = pr.Number;
        task.PullRequestUrl = pr.HtmlUrl;
        return Success(open is null ? $"Pull request creado: {pr.HtmlUrl}" : $"Se reutilizó el pull request abierto: {pr.HtmlUrl}");
    }

    private async Task<RunCompletion> MergePullRequestAsync(TaskItem task, CancellationToken ct)
    {
        if (task.PullRequestNumber is not { } number)
            return Failure("La tarea no tiene pull request para hacer merge.");
        var p = task.Project;
        var pr = await github.GetPullRequestAsync(p.RepoOwner, p.RepoName, number, ct);
        if (pr.Merged) return Success($"El pull request #{number} ya estaba integrado.");
        await github.MergePullRequestAsync(p.RepoOwner, p.RepoName, number, $"#{task.Id} {task.Title}", ct);
        return Success($"Merge (squash) del pull request #{number} realizado.");
    }

    private async Task<string> BuildBodyAsync(TaskItem task, CancellationToken ct)
    {
        var lastDevSummary = await db.TaskRuns
            .Where(r => r.TaskId == task.Id && r.Status == RunStatus.Succeeded && r.RunnerKind == StageKind.Agent
                        && db.Stages.Any(s => s.Id == r.StageId && s.PushChanges))
            .OrderByDescending(r => r.Id)
            .Select(r => r.Summary)
            .FirstOrDefaultAsync(ct);

        var publicUrl = config["App:PublicUrl"]?.TrimEnd('/');
        var link = string.IsNullOrEmpty(publicUrl) ? "" : $"\n\n[Ver tarea en DevArv]({publicUrl}/?task={task.Id})";

        var body = $"""
            ## Tarea #{task.Id}: {task.Title}

            {task.Description}

            ## Resumen del agente desarrollador

            {lastDevSummary ?? "_(sin resumen)_"}
            {link}

            ---
            _PR generado automáticamente por DevArv con Claude Code._
            """;
        // Límite de GitHub para el cuerpo de un PR: 65 536 caracteres.
        return body.Length > 60_000 ? body[..60_000] + "\n\n_(recortado)_" : body;
    }
}
