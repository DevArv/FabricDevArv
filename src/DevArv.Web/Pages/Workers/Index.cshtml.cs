using System.ComponentModel.DataAnnotations;
using DevArv.Web.Api;
using DevArv.Web.Data;
using DevArv.Web.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace DevArv.Web.Pages.Workers;

public sealed class IndexModel(AppDbContext db) : PageModel
{
    public List<WorkerNode> Workers { get; private set; } = [];
    public Dictionary<int, string> CurrentRuns { get; private set; } = [];

    [BindProperty, Required(ErrorMessage = "Escribe un nombre."), MaxLength(100)]
    [RegularExpression(@"^[A-Za-z0-9._\-]+$", ErrorMessage = "Usa letras, números, punto, guion o guion bajo.")]
    public string NewName { get; set; } = "";

    [TempData] public string? NewKey { get; set; }
    [TempData] public string? NewKeyWorker { get; set; }
    [TempData] public string? Error { get; set; }

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            await LoadAsync(ct);
            return Page();
        }
        if (await db.Workers.AnyAsync(w => w.Name == NewName, ct))
        {
            Error = $"Ya existe una PC llamada «{NewName}».";
            return RedirectToPage();
        }
        var key = ApiKeys.Generate();
        db.Workers.Add(new WorkerNode { Name = NewName.Trim(), KeyHash = ApiKeys.Hash(key), KeyPrefix = key[..10] });
        await db.SaveChangesAsync(ct);
        NewKey = key;
        NewKeyWorker = NewName.Trim();
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRegenerateAsync(int id, CancellationToken ct)
    {
        var worker = await db.Workers.FindAsync([id], ct);
        if (worker is null) return NotFound();
        var key = ApiKeys.Generate();
        worker.KeyHash = ApiKeys.Hash(key);
        worker.KeyPrefix = key[..10];
        await db.SaveChangesAsync(ct);
        NewKey = key;
        NewKeyWorker = worker.Name;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleAsync(int id, CancellationToken ct)
    {
        var worker = await db.Workers.FindAsync([id], ct);
        if (worker is null) return NotFound();
        worker.IsEnabled = !worker.IsEnabled;
        await db.SaveChangesAsync(ct);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken ct)
    {
        if (await db.TaskRuns.AnyAsync(r => r.WorkerId == id && r.Status == RunStatus.Running, ct))
        {
            Error = "La PC tiene una ejecución en curso; cancélala o espera a que termine.";
            return RedirectToPage();
        }
        var worker = await db.Workers.FindAsync([id], ct);
        if (worker is not null)
        {
            db.Workers.Remove(worker);
            await db.SaveChangesAsync(ct);
        }
        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        Workers = await db.Workers.AsNoTracking().OrderBy(w => w.Name).ToListAsync(ct);
        var running = await db.TaskRuns.AsNoTracking()
            .Where(r => r.Status == RunStatus.Running && r.WorkerId != null)
            .OrderByDescending(r => r.Id)
            .Select(r => new { WorkerId = r.WorkerId!.Value, r.TaskId, r.StageName })
            .ToListAsync(ct);
        CurrentRuns = running
            .GroupBy(r => r.WorkerId)
            .ToDictionary(g => g.Key, g => string.Join(", ", g.Select(r => $"#{r.TaskId} · {r.StageName}")));
    }
}
