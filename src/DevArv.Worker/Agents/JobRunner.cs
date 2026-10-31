using DevArv.Contracts;
using DevArv.Worker.Infrastructure;

namespace DevArv.Worker.Agents;

/// <summary>Ejecuta un trabajo completo: preparar repositorio → adjuntos → Claude → commit/push → reportar.</summary>
public sealed class JobRunner(ServerClient server, GitWorkspace git, ClaudeRunner claude, ILogger<JobRunner> logger)
{
    public async Task RunAsync(AgentJob job, CancellationToken stoppingToken)
    {
        using var jobCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        jobCts.CancelAfter(TimeSpan.FromMinutes(Math.Max(1, job.TimeoutMinutes)));
        var shipper = new LogShipper(server, job.RunId, jobCts, logger);
        RunCompletion completion;

        logger.LogInformation("Tarea #{TaskId} · {Stage} · ejecución {RunId}", job.TaskId, job.StageName, job.RunId);
        shipper.Write($"▶ {job.StageName} · tarea #{job.TaskId} «{job.TaskTitle}» · {job.Repo.Owner}/{job.Repo.Name} · rama {job.WorkBranch}");
        shipper.Write($"PC: {Environment.MachineName}");

        ClaudeOutcome? outcome = null;
        try
        {
            var ws = await git.PrepareAsync(job, shipper.Write, jobCts.Token);

            foreach (var a in job.Attachments)
            {
                var target = Path.Combine(ws.AttachmentsPath, a.LocalFileName);
                await server.DownloadAttachmentAsync(job.RunId, a.Id, target, jobCts.Token);
            }
            if (job.Attachments.Count > 0) shipper.Write($"{job.Attachments.Count} adjunto(s) descargado(s).");

            outcome = await claude.RunAsync(job, ws, shipper.Write, jobCts.Token);
            if (!outcome.Success)
            {
                completion = Failure(Describe(outcome), outcome, await git.HeadAsync(ws, CancellationToken.None));
            }
            else
            {
                var push = job.PushChanges
                    ? await git.CommitAndPushAsync(job, ws, shipper.Write, jobCts.Token)
                    : new PushResult(await git.HeadAsync(ws, jobCts.Token) ?? "", false, []);
                completion = Success(outcome, push);
                shipper.Write("✔ Etapa completada.");
            }
        }
        catch (OperationCanceledException) when (shipper.CancelledByServer)
        {
            shipper.Write("■ Cancelado desde el tablero.");
            completion = Failure("Cancelado desde el tablero.", outcome, null) with { Cancelled = true };
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            shipper.Write("■ El worker se está deteniendo.");
            completion = Failure("El worker se detuvo durante la ejecución.", outcome, null);
        }
        catch (OperationCanceledException) when (jobCts.IsCancellationRequested)
        {
            shipper.Write($"■ Tiempo máximo excedido ({job.TimeoutMinutes} min).");
            completion = Failure($"Se excedió el tiempo máximo de {job.TimeoutMinutes} minutos.", outcome, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error ejecutando la tarea #{TaskId}", job.TaskId);
            shipper.Write("✖ " + ex.Message);
            completion = Failure(ex.Message, outcome, null);
        }

        await shipper.DisposeAsync();
        await ReportAsync(job.RunId, completion);
    }

    private async Task ReportAsync(long runId, RunCompletion completion)
    {
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await server.CompleteAsync(runId, completion, timeout.Token);
                return;
            }
            catch (Exception ex) when (attempt < 5)
            {
                logger.LogWarning(ex, "No se pudo reportar la ejecución {RunId} (intento {Attempt})", runId, attempt);
                await Task.Delay(TimeSpan.FromSeconds(3 * attempt));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Se perdió el resultado de la ejecución {RunId}; el servidor la marcará como fallida.", runId);
            }
        }
    }

    private static string Describe(ClaudeOutcome o)
    {
        if (o.Result is null)
            return $"Claude terminó con código {o.ExitCode} sin resultado. {Tail(o.StdErrTail)}".Trim();
        return o.Result.Subtype switch
        {
            "error_max_turns" => "Claude alcanzó el máximo de turnos sin terminar.",
            _ => $"Claude reportó un error ({o.Result.Subtype ?? "desconocido"}). {Tail(o.Result.ResultText ?? o.StdErrTail)}".Trim(),
        };
    }

    private static string Tail(string? text) =>
        string.IsNullOrWhiteSpace(text) ? "" : text.Length > 800 ? "…" + text[^800..] : text;

    private static RunCompletion Success(ClaudeOutcome o, PushResult push) => new(
        Success: true,
        Cancelled: false,
        ErrorMessage: null,
        ResultText: o.Result?.ResultText,
        CostUsd: o.Result?.CostUsd ?? 0,
        InputTokens: o.Result?.InputTokens ?? 0,
        OutputTokens: o.Result?.OutputTokens ?? 0,
        CacheReadTokens: o.Result?.CacheReadTokens ?? 0,
        CacheWriteTokens: o.Result?.CacheWriteTokens ?? 0,
        NumTurns: o.Result?.NumTurns,
        SessionId: o.Result?.SessionId,
        Model: o.Model,
        HeadCommit: string.IsNullOrEmpty(push.HeadCommit) ? null : push.HeadCommit,
        Pushed: push.Pushed,
        ChangedFiles: push.ChangedFiles);

    private static RunCompletion Failure(string message, ClaudeOutcome? o, string? head) => new(
        Success: false,
        Cancelled: false,
        ErrorMessage: message,
        ResultText: o?.Result?.ResultText,
        CostUsd: o?.Result?.CostUsd ?? 0,
        InputTokens: o?.Result?.InputTokens ?? 0,
        OutputTokens: o?.Result?.OutputTokens ?? 0,
        CacheReadTokens: o?.Result?.CacheReadTokens ?? 0,
        CacheWriteTokens: o?.Result?.CacheWriteTokens ?? 0,
        NumTurns: o?.Result?.NumTurns,
        SessionId: o?.Result?.SessionId,
        Model: o?.Model,
        HeadCommit: head,
        Pushed: false,
        ChangedFiles: []);
}
