using DevArv.Contracts.Models;
using DevArv.Web.DataModels;
using DevArv.Web.DatabaseConnection;
using DevArv.Web.Enums.Runs;
using DevArv.Web.Enums.Workflow;
using DevArv.Web.Helpers;
using DevArv.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DevArv.Web.Services;

/// <summary>Acciones automáticas del servidor sobre GitHub.</summary>
public sealed class SystemActionExecutor(AppDbContext Db, GitHubService GitHub, IOptions<AppOptions> AppOptionsAccessor)
{
    // Límite de GitHub para el cuerpo de un PR: 65 536 caracteres.
    private const int MAX_PULL_REQUEST_BODY_CHARS = 60_000;

    public static RunCompletion Failure(string Message) =>
        new(false, false, Message, null, 0, 0, 0, 0, 0, null, null, null, null, false, []);

    private static RunCompletion Success(string Text) =>
        new(true, false, null, Text, 0, 0, 0, 0, 0, null, null, null, null, false, []);

    public async Task<RunCompletion> ExecuteAsync(TaskRun Run, CancellationToken CT)
    {
        var stage = await Db.Stages.FindAsync([Run.StageID], CT)
                    ?? throw new InvalidOperationException("La columna de la ejecución ya no existe.");
        var task = Run.Task;
        return stage.SystemAction switch
        {
            SystemAction.CREATE_PULL_REQUEST => await CreatePullRequestAsync(task, CT),
            SystemAction.MERGE_PULL_REQUEST => await MergePullRequestAsync(task, CT),
            _ => Success("Columna de sistema sin acción configurada; se avanza."),
        };
    }

    private async Task<RunCompletion> CreatePullRequestAsync(TaskItem Item, CancellationToken CT)
    {
        var project = Item.Project;
        if (string.IsNullOrEmpty(Item.WorkBranch))
            return Failure("La tarea no tiene rama de trabajo; primero debe pasar por un agente desarrollador.");

        if (Item.PullRequestNumber is { } existingNumber)
        {
            var existing = await GitHub.GetPullRequestAsync(
                Owner: project.RepoOwner,
                Repo: project.RepoName,
                Number: existingNumber,
                CT: CT);
            if (existing.State == "open")
                return Success($"El pull request ya existe y sigue abierto: {existing.HtmlUrl}");
        }

        var branchExists = await GitHub.BranchExistsAsync(
            Owner: project.RepoOwner,
            Repo: project.RepoName,
            Branch: Item.WorkBranch,
            CT: CT);
        if (!branchExists)
            return Failure($"La rama {Item.WorkBranch} no existe en GitHub. ¿El desarrollador hizo push?");

        var open = await GitHub.FindOpenPullRequestAsync(
            Owner: project.RepoOwner,
            Repo: project.RepoName,
            Head: Item.WorkBranch,
            CT: CT);
        var pr = open ?? await GitHub.CreatePullRequestAsync(
            Owner: project.RepoOwner,
            Repo: project.RepoName,
            Head: Item.WorkBranch,
            Base: Item.BaseBranch,
            Title: PullRequestTitle.FromBranch(Item.WorkBranch),
            Body: await BuildBodyAsync(Item, CT),
            CT: CT);

        Item.PullRequestNumber = pr.Number;
        Item.PullRequestUrl = pr.HtmlUrl;
        Item.PullRequestMergedAt = null;
        // El orquestador la actualiza cuando GitHub termina de calcular si el pull request se puede integrar.
        Item.PullRequestHasConflicts = pr.HasConflicts ?? false;
        return Success(open is null ? $"Pull request creado: {pr.HtmlUrl}" : $"Se reutilizó el pull request abierto: {pr.HtmlUrl}");
    }

    private async Task<RunCompletion> MergePullRequestAsync(TaskItem Item, CancellationToken CT)
    {
        if (Item.PullRequestNumber is not { } number)
            return Failure("La tarea no tiene pull request para hacer merge.");
        var project = Item.Project;
        var pr = await GitHub.GetPullRequestAsync(
            Owner: project.RepoOwner,
            Repo: project.RepoName,
            Number: number,
            CT: CT);
        // La columna de merge ya decide el destino (su ✓); se marca para que el orquestador no la mueva otra vez.
        if (pr.Merged)
        {
            Item.PullRequestMergedAt ??= DateTimeOffset.UtcNow;
            Item.PullRequestHasConflicts = false;
            return Success($"El pull request #{number} ya estaba integrado.");
        }
        await GitHub.MergePullRequestAsync(
            Owner: project.RepoOwner,
            Repo: project.RepoName,
            Number: number,
            CommitTitle: $"#{Item.ID} {Item.Title}",
            CT: CT);
        Item.PullRequestMergedAt = DateTimeOffset.UtcNow;
        Item.PullRequestHasConflicts = false;
        return Success($"Merge (squash) del pull request #{number} realizado.");
    }

    private async Task<string> BuildBodyAsync(TaskItem Item, CancellationToken CT)
    {
        var lastDevSummary = await Db.TaskRuns
            .Where(r => r.TaskID == Item.ID && r.Status == RunStatus.SUCCEEDED && r.RunnerKind == StageKind.AGENT
                        && Db.Stages.Any(s => s.ID == r.StageID && s.PushChanges))
            .OrderByDescending(r => r.ID)
            .Select(r => r.Summary)
            .FirstOrDefaultAsync(CT);

        var publicUrl = AppOptionsAccessor.Value.PublicUrl.Trim().TrimEnd('/');
        var link = string.IsNullOrEmpty(publicUrl) ? "" : $"\n\n[Ver tarea en DevArv]({publicUrl}/?task={Item.ID})";

        var body = $"""
            ## Tarea #{Item.ID}: {Item.Title}

            {Item.Description}

            ## Resumen del agente desarrollador

            {lastDevSummary ?? "_(sin resumen)_"}
            {link}

            ---
            _PR generado automáticamente por DevArv con Claude Code._
            """;
        return body.Length > MAX_PULL_REQUEST_BODY_CHARS ? body[..MAX_PULL_REQUEST_BODY_CHARS] + "\n\n_(recortado)_" : body;
    }
}
