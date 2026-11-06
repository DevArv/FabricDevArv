using DevArv.Contracts.Models;
using DevArv.Worker.Models;

namespace DevArv.Worker.Services;

/// <summary>Ejecuta un trabajo completo: preparar repositorio → adjuntos → Claude → commit/push → reportar.</summary>
public sealed class JobRunner(ServerClient Server, GitWorkspace Git, ClaudeRunner Claude, ILogger<JobRunner> Logger)
{
    public async Task RunAsync(AgentJob Job, CancellationToken StoppingToken)
    {
        using var jobCts = CancellationTokenSource.CreateLinkedTokenSource(StoppingToken);
        jobCts.CancelAfter(TimeSpan.FromMinutes(Math.Max(1, Job.TimeoutMinutes)));
        var shipper = new LogShipper(Server, Job.RunID, jobCts, Logger);
        RunCompletion completion;

        Logger.LogInformation("Tarea #{TaskId} · {Stage} · ejecución {RunId}", Job.TaskID, Job.StageName, Job.RunID);
        shipper.Write($"▶ {Job.StageName} · tarea #{Job.TaskID} «{Job.TaskTitle}» · {Job.Repo.Owner}/{Job.Repo.Name} · rama {Job.WorkBranch}");
        shipper.Write($"PC: {Environment.MachineName}");

        ClaudeOutcome? outcome = null;
        try
        {
            var ws = await Git.PrepareAsync(
                Job: Job,
                Log: shipper.Write,
                CT: jobCts.Token);

            foreach (var a in Job.Attachments)
            {
                var target = Path.Combine(ws.AttachmentsPath, a.LocalFileName);
                await Server.DownloadAttachmentAsync(
                    RunID: Job.RunID,
                    AttachmentID: a.ID,
                    TargetPath: target,
                    CT: jobCts.Token);
            }
            if (Job.Attachments.Count > 0) shipper.Write($"{Job.Attachments.Count} adjunto(s) descargado(s).");

            outcome = await Claude.RunAsync(
                Job: Job,
                Workspace: ws,
                Log: shipper.Write,
                CT: jobCts.Token);
            if (!outcome.Success)
            {
                completion = Failure(
                    Message: Describe(outcome),
                    Outcome: outcome,
                    Head: await Git.HeadAsync(ws, CancellationToken.None));
            }
            else
            {
                var push = Job.PushChanges
                    ? await Git.CommitAndPushAsync(
                        Job: Job,
                        Workspace: ws,
                        Log: shipper.Write,
                        CT: jobCts.Token)
                    : new PushResult(await Git.HeadAsync(ws, jobCts.Token) ?? "", false, []);
                completion = Success(outcome, push);
                shipper.Write("✔ Etapa completada.");
            }
        }
        catch (OperationCanceledException) when (shipper.CancelledByServer)
        {
            shipper.Write("■ Cancelado desde el tablero.");
            completion = Failure(
                Message: "Cancelado desde el tablero.",
                Outcome: outcome,
                Head: null) with { Cancelled = true };
        }
        catch (OperationCanceledException) when (StoppingToken.IsCancellationRequested)
        {
            shipper.Write("■ El worker se está deteniendo.");
            completion = Failure(
                Message: "El worker se detuvo durante la ejecución.",
                Outcome: outcome,
                Head: null);
        }
        catch (OperationCanceledException) when (jobCts.IsCancellationRequested)
        {
            shipper.Write($"■ Tiempo máximo excedido ({Job.TimeoutMinutes} min).");
            completion = Failure(
                Message: $"Se excedió el tiempo máximo de {Job.TimeoutMinutes} minutos.",
                Outcome: outcome,
                Head: null);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error ejecutando la tarea #{TaskId}", Job.TaskID);
            shipper.Write("✖ " + ex.Message);
            completion = Failure(
                Message: ex.Message,
                Outcome: outcome,
                Head: null);
        }

        await shipper.DisposeAsync();
        await ReportAsync(Job.RunID, completion);
    }

    private async Task ReportAsync(long RunID, RunCompletion Completion)
    {
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await Server.CompleteAsync(
                    RunID: RunID,
                    Completion: Completion,
                    CT: timeout.Token);
                return;
            }
            catch (Exception ex) when (attempt < 5)
            {
                Logger.LogWarning(ex, "No se pudo reportar la ejecución {RunId} (intento {Attempt})", RunID, attempt);
                await Task.Delay(TimeSpan.FromSeconds(3 * attempt));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Se perdió el resultado de la ejecución {RunId}; el servidor la marcará como fallida.", RunID);
            }
        }
    }

    private static string Describe(ClaudeOutcome Outcome)
    {
        if (Outcome.Result is null)
            return $"Claude terminó con código {Outcome.ExitCode} sin resultado. {Tail(Outcome.StdErrTail)}".Trim();
        return Outcome.Result.Subtype switch
        {
            "error_max_turns" => "Claude alcanzó el máximo de turnos sin terminar.",
            _ => $"Claude reportó un error ({Outcome.Result.Subtype ?? "desconocido"}). {Tail(Outcome.Result.ResultText ?? Outcome.StdErrTail)}".Trim(),
        };
    }

    private static string Tail(string? Text) =>
        string.IsNullOrWhiteSpace(Text) ? "" : Text.Length > 800 ? "…" + Text[^800..] : Text;

    private static RunCompletion Success(ClaudeOutcome Outcome, PushResult Push) => new(
        Success: true,
        Cancelled: false,
        ErrorMessage: null,
        ResultText: Outcome.Result?.ResultText,
        CostUsd: Outcome.Result?.CostUsd ?? 0,
        InputTokens: Outcome.Result?.InputTokens ?? 0,
        OutputTokens: Outcome.Result?.OutputTokens ?? 0,
        CacheReadTokens: Outcome.Result?.CacheReadTokens ?? 0,
        CacheWriteTokens: Outcome.Result?.CacheWriteTokens ?? 0,
        NumTurns: Outcome.Result?.NumTurns,
        SessionID: Outcome.Result?.SessionID,
        Model: Outcome.Model,
        HeadCommit: string.IsNullOrEmpty(Push.HeadCommit) ? null : Push.HeadCommit,
        Pushed: Push.Pushed,
        ChangedFiles: Push.ChangedFiles);

    private static RunCompletion Failure(string Message, ClaudeOutcome? Outcome, string? Head) => new(
        Success: false,
        Cancelled: false,
        ErrorMessage: Message,
        ResultText: Outcome?.Result?.ResultText,
        CostUsd: Outcome?.Result?.CostUsd ?? 0,
        InputTokens: Outcome?.Result?.InputTokens ?? 0,
        OutputTokens: Outcome?.Result?.OutputTokens ?? 0,
        CacheReadTokens: Outcome?.Result?.CacheReadTokens ?? 0,
        CacheWriteTokens: Outcome?.Result?.CacheWriteTokens ?? 0,
        NumTurns: Outcome?.Result?.NumTurns,
        SessionID: Outcome?.Result?.SessionID,
        Model: Outcome?.Model,
        HeadCommit: Head,
        Pushed: false,
        ChangedFiles: []);
}
