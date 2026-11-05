using DevArv.Contracts.Models;
using DevArv.Web.DatabaseConnection;
using DevArv.Web.Enums.Runs;
using DevArv.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace DevArv.Web.Api;

/// <summary>API REST que consumen los workers (PCs) que ejecutan Claude Code.</summary>
public static class WorkerApi
{
    private const int MaxLogChars = 2_000_000;

    public static IEndpointRouteBuilder MapWorkerApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/worker")
            .AddEndpointFilter<WorkerAuthFilter>()
            .DisableAntiforgery();

        group.MapPost("/heartbeat", HeartbeatAsync);
        group.MapPost("/claim", ClaimAsync);
        group.MapPost("/runs/{runId:long}/log", AppendLogAsync);
        group.MapPost("/runs/{runId:long}/complete", CompleteAsync);
        group.MapGet("/runs/{runId:long}/attachments/{attachmentId:long}", DownloadAttachmentAsync);
        return app;
    }

    private static async Task<IResult> HeartbeatAsync(HeartbeatRequest request, HttpContext http, AppDbContext db, CancellationToken ct)
    {
        var worker = WorkerAuthFilter.Current(http);
        worker.MachineName = Limit(request.MachineName, 100);
        worker.Version = Limit(request.Version, 40);
        worker.OsDescription = Limit(request.OsDescription, 200);
        worker.RunningJobs = Math.Max(0, request.RunningJobs);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new HeartbeatResponse(DateTimeOffset.UtcNow, worker.Name));
    }

    private static async Task<IResult> ClaimAsync(
        ClaimRequest request, HttpContext http, AppDbContext db, WorkflowService workflow,
        PromptBuilder prompts, GitHubService github, CancellationToken ct)
    {
        var worker = WorkerAuthFilter.Current(http);
        await db.SaveChangesAsync(ct); // persiste LastSeenAt

        var run = await workflow.ClaimNextAgentRunAsync(worker, ct);
        if (run is null) return Results.NoContent();

        var task = run.Task;
        var stage = await db.Stages.FindAsync([run.StageId], ct);
        if (stage is null || string.IsNullOrEmpty(task.WorkBranch))
        {
            await workflow.CompleteRunAsync(run.Id,
                SystemActionExecutor.Failure("La columna o la rama de trabajo no existen."), worker.Name, ct);
            return Results.NoContent();
        }

        var prompt = prompts.Build(task, stage);
        run.PromptSnapshot = prompt;
        await db.SaveChangesAsync(ct);

        var job = new AgentJob(
            RunId: run.Id,
            TaskId: task.Id,
            TaskTitle: task.Title,
            StageName: stage.Name,
            Repo: new RepoInfo(task.Project.RepoOwner, task.Project.RepoName,
                github.CloneUrl(task.Project.RepoOwner, task.Project.RepoName)),
            BaseBranch: task.BaseBranch,
            WorkBranch: task.WorkBranch,
            Prompt: prompt,
            AppendSystemPrompt: PromptBuilder.BuildSystemAppend(task, stage),
            Model: run.Model,
            PermissionMode: string.IsNullOrWhiteSpace(stage.PermissionMode) ? "default" : stage.PermissionMode,
            AllowedTools: PromptBuilder.BuildAllowedTools(stage),
            DisallowedTools: Lines(stage.DisallowedTools),
            MaxTurns: stage.MaxTurns,
            ExtraArgs: Lines(stage.ExtraArgs),
            PushChanges: stage.PushChanges,
            TimeoutMinutes: stage.TimeoutMinutes <= 0 ? 60 : stage.TimeoutMinutes,
            CommitMessage: $"DevArv #{task.Id}: {task.Title}",
            Attachments: task.Attachments
                .OrderBy(a => a.Id)
                .Select(a => new AttachmentInfo(a.Id, a.FileName, a.ContentType, a.Size))
                .ToList());

        return Results.Ok(job);
    }

    private static async Task<IResult> AppendLogAsync(long runId, LogChunk chunk, HttpContext http, AppDbContext db, CancellationToken ct)
    {
        var worker = WorkerAuthFilter.Current(http);
        await db.SaveChangesAsync(ct);

        var run = await db.TaskRuns.AsNoTracking()
            .Where(r => r.Id == runId && r.WorkerId == worker.Id)
            .Select(r => new { r.Status, r.CancelRequested })
            .FirstOrDefaultAsync(ct);
        if (run is null) return Results.NotFound();
        if (run.Status != RunStatus.Running) return Results.Ok(new LogChunkResponse(CancelRequested: true));

        var text = WorkflowService.Clean(chunk.Text) ?? "";
        DateTimeOffset? now = DateTimeOffset.UtcNow;
        if (text.Length > 0)
        {
            await db.TaskRuns
                .Where(r => r.Id == runId && r.Log.Length < MaxLogChars)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Log, r => r.Log + text)
                    .SetProperty(r => r.LastActivityAt, now), ct);
        }
        else
        {
            await db.TaskRuns
                .Where(r => r.Id == runId)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.LastActivityAt, now), ct);
        }

        return Results.Ok(new LogChunkResponse(run.CancelRequested));
    }

    private static async Task<IResult> CompleteAsync(
        long runId, RunCompletion completion, HttpContext http, AppDbContext db, WorkflowService workflow, CancellationToken ct)
    {
        var worker = WorkerAuthFilter.Current(http);
        await db.SaveChangesAsync(ct);

        var owns = await db.TaskRuns.AnyAsync(r => r.Id == runId && r.WorkerId == worker.Id, ct);
        if (!owns) return Results.NotFound();

        await workflow.CompleteRunAsync(runId, completion, worker.Name, ct);
        return Results.Ok();
    }

    private static async Task<IResult> DownloadAttachmentAsync(
        long runId, long attachmentId, HttpContext http, AppDbContext db, AttachmentStorage storage, CancellationToken ct)
    {
        var worker = WorkerAuthFilter.Current(http);
        var attachment = await db.TaskRuns
            .Where(r => r.Id == runId && r.WorkerId == worker.Id && r.Status == RunStatus.Running)
            .SelectMany(r => r.Task.Attachments)
            .FirstOrDefaultAsync(a => a.Id == attachmentId, ct);
        if (attachment is null) return Results.NotFound();

        var path = storage.PathOf(attachment);
        return File.Exists(path)
            ? TypedResults.PhysicalFile(path, attachment.ContentType, attachment.FileName)
            : Results.NotFound();
    }

    private static List<string> Lines(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(l => !l.StartsWith('#'))
                .ToList();

    private static string? Limit(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max];
}
