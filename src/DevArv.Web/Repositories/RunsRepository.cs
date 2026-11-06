using DevArv.Contracts.Models;
using DevArv.Web.DatabaseConnection;
using DevArv.Web.Enums.Runs;
using DevArv.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace DevArv.Web.Repositories;

/// <summary>Lecturas y escrituras de las ejecuciones (<c>TaskRun</c>) que hacen las PCs mientras trabajan.</summary>
public class RunsRepository(AppDbContext Db)
{
    private const int MAX_LOG_CHARS = 2_000_000;

    /// <summary>Si la ejecución existe y fue tomada por esa PC.</summary>
    public Task<bool> IsAssignedToWorkerAsync(long RunID, int WorkerID, CancellationToken CT) =>
        Db.TaskRuns.AnyAsync(r => r.Id == RunID && r.WorkerId == WorkerID, CT);

    /// <summary>
    /// Agrega texto al log de la ejecución (hasta <see cref="MAX_LOG_CHARS"/>) y marca actividad.
    /// Devuelve null si la ejecución no es de esa PC. Si ya no está en curso responde que se canceló:
    /// el worker detiene su proceso.
    /// </summary>
    public async Task<LogChunkResponse?> AppendLogAsync(long RunID, int WorkerID, string? Text, CancellationToken CT)
    {
        var run = await Db.TaskRuns.AsNoTracking()
            .Where(r => r.Id == RunID && r.WorkerId == WorkerID)
            .Select(r => new { r.Status, r.CancelRequested })
            .FirstOrDefaultAsync(CT);
        if (run is null) return null;
        if (run.Status != RunStatus.Running) return new LogChunkResponse(CancelRequested: true);

        await WriteLogAsync(
            RunID: RunID,
            Text: WorkflowService.Clean(Text) ?? "",
            Now: DateTimeOffset.UtcNow,
            CT: CT);
        return new LogChunkResponse(run.CancelRequested);
    }

    /// <summary>
    /// Concatena <paramref name="Text"/> al log con un UPDATE atómico (varios envíos simultáneos no se pisan) y
    /// actualiza la última actividad. Es virtual sólo para probar <see cref="AppendLogAsync"/> sin PostgreSQL:
    /// el proveedor en memoria no admite ExecuteUpdate.
    /// </summary>
    protected virtual async Task WriteLogAsync(long RunID, string Text, DateTimeOffset Now, CancellationToken CT)
    {
        DateTimeOffset? now = Now;
        if (Text.Length > 0)
        {
            await Db.TaskRuns
                .Where(r => r.Id == RunID && r.Log.Length < MAX_LOG_CHARS)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Log, r => r.Log + Text)
                    .SetProperty(r => r.LastActivityAt, now), CT);
        }
        else
        {
            await Db.TaskRuns
                .Where(r => r.Id == RunID)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.LastActivityAt, now), CT);
        }
    }
}
