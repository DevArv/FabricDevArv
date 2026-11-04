using DevArv.Web.DataModels;
using DevArv.Web.DatabaseConnection;
using DevArv.Web.Enums.Runs;
using DevArv.Web.Helpers;
using Microsoft.EntityFrameworkCore;

namespace DevArv.Web.Repositories;

public class WorkersRepository(AppDbContext db)
{
    public Task<List<WorkerNode>> GetAllAsync(CancellationToken ct) =>
        db.Workers.AsNoTracking().OrderBy(w => w.Name).ToListAsync(ct);

    public Task<List<WorkerNode>> GetEnabledAsync(CancellationToken ct) =>
        db.Workers.AsNoTracking().Where(w => w.IsEnabled).OrderBy(w => w.Name).ToListAsync(ct);

    /// <summary>Por PC: texto con las ejecuciones en curso ("#12 · Desarrollador").</summary>
    public async Task<Dictionary<int, string>> GetCurrentRunsAsync(CancellationToken ct)
    {
        var running = await db.TaskRuns.AsNoTracking()
            .Where(r => r.Status == RunStatus.Running && r.WorkerId != null)
            .OrderByDescending(r => r.Id)
            .Select(r => new { WorkerId = r.WorkerId!.Value, r.TaskId, r.StageName })
            .ToListAsync(ct);
        return running
            .GroupBy(r => r.WorkerId)
            .ToDictionary(g => g.Key, g => string.Join(", ", g.Select(r => $"#{r.TaskId} · {r.StageName}")));
    }

    /// <summary>Registra una PC y devuelve su API key en claro (única vez que se conoce).</summary>
    public async Task<string> InsertAsync(string name, CancellationToken ct)
    {
        name = name.Trim();
        if (await db.Workers.AnyAsync(w => w.Name == name, ct))
            throw new ValidationFailedException(nameof(name), $"Ya existe una PC llamada «{name}».");

        var key = ApiKeys.Generate();
        db.Workers.Add(new WorkerNode { Name = name, KeyHash = ApiKeys.Hash(key), KeyPrefix = key[..10] });
        await db.SaveChangesAsync(ct);
        return key;
    }

    /// <summary>Genera una API key nueva. Devuelve null si la PC no existe.</summary>
    public async Task<(string Name, string Key)?> RegenerateKeyAsync(int id, CancellationToken ct)
    {
        var worker = await db.Workers.FindAsync([id], ct);
        if (worker is null) return null;
        var key = ApiKeys.Generate();
        worker.KeyHash = ApiKeys.Hash(key);
        worker.KeyPrefix = key[..10];
        await db.SaveChangesAsync(ct);
        return (worker.Name, key);
    }

    public async Task<bool> ToggleEnabledAsync(int id, CancellationToken ct)
    {
        var worker = await db.Workers.FindAsync([id], ct);
        if (worker is null) return false;
        worker.IsEnabled = !worker.IsEnabled;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        if (await db.TaskRuns.AnyAsync(r => r.WorkerId == id && r.Status == RunStatus.Running, ct))
            throw new ValidationFailedException(nameof(id), "La PC tiene una ejecución en curso; cancélala o espera a que termine.");

        var worker = await db.Workers.FindAsync([id], ct);
        if (worker is null) return;
        db.Workers.Remove(worker);
        await db.SaveChangesAsync(ct);
    }
}
