using DevArv.Contracts.Models;
using DevArv.Web.DataModels;
using DevArv.Web.Repositories;
using DevArv.Web.Services;

namespace DevArv.Web.Api;

/// <summary>API REST que consumen los workers (PCs) que ejecutan Claude Code.</summary>
public static class WorkerApi
{
    private const int DEFAULT_TIMEOUT_MINUTES = 60;

    public static IEndpointRouteBuilder MapWorkerApi(this IEndpointRouteBuilder App)
    {
        var group = App.MapGroup("/api/worker")
            .AddEndpointFilter<WorkerAuthFilter>()
            .DisableAntiforgery();

        group.MapPost("/heartbeat", HeartbeatAsync);
        group.MapPost("/claim", ClaimAsync);
        group.MapPost("/runs/{RunID:long}/log", AppendLogAsync);
        group.MapPost("/runs/{RunID:long}/complete", CompleteAsync);
        group.MapGet("/runs/{RunID:long}/attachments/{AttachmentID:long}", DownloadAttachmentAsync);
        return App;
    }

    private static async Task<IResult> HeartbeatAsync(
        HeartbeatRequest Request, HttpContext Http, WorkersRepository Workers, CancellationToken CT)
    {
        var worker = WorkerAuthFilter.Current(Http);
        await Workers.RecordHeartbeatAsync(
            Worker: worker,
            Request: Request,
            CT: CT);
        return Results.Ok(new HeartbeatResponse(DateTimeOffset.UtcNow, worker.Name));
    }

    private static async Task<IResult> ClaimAsync(
        ClaimRequest Request, HttpContext Http, WorkflowService Workflow, PromptBuilder Prompts, GitHubService GitHub,
        CancellationToken CT)
    {
        var worker = WorkerAuthFilter.Current(Http);

        var run = await Workflow.ClaimNextAgentRunAsync(
            Worker: worker,
            CT: CT);
        if (run is null) return Results.NoContent();

        var task = run.Task;
        var stage = await Workflow.GetStageAsync(
            StageID: run.StageID,
            CT: CT);
        if (stage is null || string.IsNullOrEmpty(task.WorkBranch))
        {
            await Workflow.CompleteRunAsync(
                RunID: run.ID,
                Completion: SystemActionExecutor.Failure("La columna o la rama de trabajo no existen."),
                Actor: worker.Name,
                CT: CT);
            return Results.NoContent();
        }

        var mergeBaseBranch = run.ResolveConflicts && stage.PushChanges;
        var prompt = Prompts.Build(
            Item: task,
            Stage: stage,
            ResolveConflicts: mergeBaseBranch);
        await Workflow.SavePromptSnapshotAsync(
            Run: run,
            Prompt: prompt,
            CT: CT);

        var job = new AgentJob(
            RunID: run.ID,
            TaskID: task.ID,
            TaskTitle: task.Title,
            StageName: stage.Name,
            Repo: new RepoInfo(task.Project.RepoOwner, task.Project.RepoName,
                GitHub.CloneUrl(task.Project.RepoOwner, task.Project.RepoName)),
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
            TimeoutMinutes: stage.TimeoutMinutes <= 0 ? DEFAULT_TIMEOUT_MINUTES : stage.TimeoutMinutes,
            CommitMessage: $"DevArv #{task.ID}: {task.Title}",
            Attachments: JobAttachments(task),
            MergeBaseBranch: mergeBaseBranch);

        return Results.Ok(job);
    }

    /// <summary>
    /// Adjuntos que el worker descarga para la tarea. Las capturas de la pestaña «Capturas» muestran el resultado, no
    /// definen la tarea: no van al agente.
    /// </summary>
    internal static List<AttachmentInfo> JobAttachments(TaskItem Item) =>
        Item.Attachments
            .Where(a => !a.IsCapture)
            .OrderBy(a => a.ID)
            .Select(a => new AttachmentInfo(a.ID, a.FileName, a.ContentType, a.Size))
            .ToList();

    private static async Task<IResult> AppendLogAsync(
        long RunID, LogChunk Chunk, HttpContext Http, RunsRepository Runs, CancellationToken CT)
    {
        var worker = WorkerAuthFilter.Current(Http);
        var response = await Runs.AppendLogAsync(
            RunID: RunID,
            WorkerID: worker.ID,
            Text: Chunk.Text,
            CT: CT);
        return response is null ? Results.NotFound() : Results.Ok(response);
    }

    private static async Task<IResult> CompleteAsync(
        long RunID, RunCompletion Completion, HttpContext Http, RunsRepository Runs, WorkflowService Workflow,
        CancellationToken CT)
    {
        var worker = WorkerAuthFilter.Current(Http);

        var owns = await Runs.IsAssignedToWorkerAsync(
            RunID: RunID,
            WorkerID: worker.ID,
            CT: CT);
        if (!owns) return Results.NotFound();

        await Workflow.CompleteRunAsync(
            RunID: RunID,
            Completion: Completion,
            Actor: worker.Name,
            CT: CT);
        return Results.Ok();
    }

    private static async Task<IResult> DownloadAttachmentAsync(
        long RunID, long AttachmentID, HttpContext Http, TaskAttachmentsRepository Attachments, CancellationToken CT)
    {
        var worker = WorkerAuthFilter.Current(Http);
        var attachment = await Attachments.GetFileForWorkerRunAsync(
            RunID: RunID,
            AttachmentID: AttachmentID,
            WorkerID: worker.ID,
            CT: CT);
        return attachment?.Content is null
            ? Results.NotFound()
            : TypedResults.File(attachment.Content.Data, attachment.ContentType, attachment.FileName);
    }

    private static List<string> Lines(string? Text) =>
        string.IsNullOrWhiteSpace(Text)
            ? []
            : Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(l => !l.StartsWith('#'))
                .ToList();
}
