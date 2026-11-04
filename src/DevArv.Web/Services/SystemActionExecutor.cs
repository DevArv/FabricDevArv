using DevArv.Contracts.Models;
using DevArv.Web.DataModels;
using DevArv.Web.DatabaseConnection;
using DevArv.Web.Enums.Runs;
using DevArv.Web.Enums.Workflow;
using Microsoft.EntityFrameworkCore;

namespace DevArv.Web.Services;

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
