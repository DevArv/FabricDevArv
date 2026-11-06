using DevArv.Contracts.Models;
using DevArv.Web.DataModels;
using DevArv.Web.DatabaseConnection;
using DevArv.Web.Enums.Runs;
using DevArv.Web.Helpers;
using DevArv.Web.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace DevArv.Web.Repositories;

public class WorkersRepository(AppDbContext Db)
{
    public Task<List<WorkerNode>> GetAllAsync(CancellationToken CT) =>
        Db.Workers.AsNoTracking().OrderBy(w => w.Name).ToListAsync(CT);

    public Task<List<WorkerNode>> GetEnabledAsync(CancellationToken CT) =>
        Db.Workers.AsNoTracking().Where(w => w.IsEnabled).OrderBy(w => w.Name).ToListAsync(CT);

    /// <summary>Por PC: texto con las ejecuciones en curso ("#12 · Desarrollador").</summary>
    public async Task<Dictionary<int, string>> GetCurrentRunsAsync(CancellationToken CT)
    {
        var running = await Db.TaskRuns.AsNoTracking()
            .Where(r => r.Status == RunStatus.RUNNING && r.WorkerID != null)
            .OrderByDescending(r => r.ID)
            .Select(r => new { WorkerID = r.WorkerID!.Value, r.TaskID, r.StageName })
            .ToListAsync(CT);
        return running
            .GroupBy(r => r.WorkerID)
            .ToDictionary(g => g.Key, g => string.Join(", ", g.Select(r => $"#{r.TaskID} · {r.StageName}")));
    }

    /// <summary>Registra una PC y devuelve su API key en claro (única vez que se conoce).</summary>
    public async Task<WorkerKeyViewModel> InsertAsync(WorkerCreateViewModel Record, CancellationToken CT)
    {
        var name = Record.Name.Trim();
        if (await Db.Workers.AnyAsync(w => w.Name == name, CT))
            throw new ValidationFailedException("Input.Name", $"Ya existe una PC llamada «{name}».");

        var key = ApiKeys.Generate();
        Db.Workers.Add(new WorkerNode { Name = name, KeyHash = ApiKeys.Hash(key), KeyPrefix = key[..10] });
        await Db.SaveChangesAsync(CT);
        return new WorkerKeyViewModel(name, key);
    }

    /// <summary>Genera una API key nueva. Devuelve null si la PC no existe.</summary>
    public async Task<WorkerKeyViewModel?> RegenerateKeyAsync(int ID, CancellationToken CT)
    {
        var worker = await Db.Workers.FindAsync([ID], CT);
        if (worker is null) return null;
        var key = ApiKeys.Generate();
        worker.KeyHash = ApiKeys.Hash(key);
        worker.KeyPrefix = key[..10];
        await Db.SaveChangesAsync(CT);
        return new WorkerKeyViewModel(worker.Name, key);
    }

    public async Task<bool> ToggleEnabledAsync(int ID, CancellationToken CT)
    {
        var worker = await Db.Workers.FindAsync([ID], CT);
        if (worker is null) return false;
        worker.IsEnabled = !worker.IsEnabled;
        await Db.SaveChangesAsync(CT);
        return true;
    }

    public async Task DeleteAsync(int ID, CancellationToken CT)
    {
        if (await Db.TaskRuns.AnyAsync(r => r.WorkerID == ID && r.Status == RunStatus.RUNNING, CT))
            throw new ValidationFailedException(nameof(ID), "La PC tiene una ejecución en curso; cancélala o espera a que termine.");

        var worker = await Db.Workers.FindAsync([ID], CT);
        if (worker is null) return;
        Db.Workers.Remove(worker);
        await Db.SaveChangesAsync(CT);
    }

    /// <summary>
    /// Identifica a la PC por el hash de su API key (ver <see cref="ApiKeys.Hash"/>) y registra que acaba de conectarse.
    /// Devuelve null si la clave no existe o la PC está deshabilitada.
    /// </summary>
    public async Task<WorkerNode?> AuthenticateAsync(string KeyHash, CancellationToken CT)
    {
        var worker = await Db.Workers.FirstOrDefaultAsync(w => w.KeyHash == KeyHash && w.IsEnabled, CT);
        if (worker is null) return null;

        worker.LastSeenAt = DateTimeOffset.UtcNow;
        await Db.SaveChangesAsync(CT);
        return worker;
    }

    /// <summary>Guarda los datos que la PC reporta en su latido; los textos se recortan al tamaño de su columna.</summary>
    public async Task RecordHeartbeatAsync(WorkerNode Worker, HeartbeatRequest Request, CancellationToken CT)
    {
        Worker.MachineName = Limit(
            Value: Request.MachineName,
            Max: 100);
        Worker.Version = Limit(
            Value: Request.Version,
            Max: 40);
        Worker.OsDescription = Limit(
            Value: Request.OsDescription,
            Max: 200);
        Worker.RunningJobs = Math.Max(0, Request.RunningJobs);
        await Db.SaveChangesAsync(CT);
    }

    internal static string? Limit(string? Value, int Max) =>
        Value is null ? null : Value.Length <= Max ? Value : Value[..Max];
}
