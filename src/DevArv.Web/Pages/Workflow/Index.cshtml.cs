using DevArv.Web.Data;
using DevArv.Web.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace DevArv.Web.Pages.Workflow;

public sealed class IndexModel(AppDbContext db) : PageModel
{
    public List<Stage> Stages { get; private set; } = [];
    public Dictionary<int, int> TaskCounts { get; private set; } = [];
    [TempData] public string? Error { get; set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        Stages = await db.Stages.AsNoTracking().OrderBy(s => s.Order).ToListAsync(ct);
        TaskCounts = await db.Tasks.GroupBy(t => t.StageId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
    }

    public string NameOf(int? id) => Stages.FirstOrDefault(s => s.Id == id)?.Name ?? "—";

    public async Task<IActionResult> OnPostMoveAsync(int id, int direction, CancellationToken ct)
    {
        var stages = await db.Stages.OrderBy(s => s.Order).ToListAsync(ct);
        var index = stages.FindIndex(s => s.Id == id);
        var other = index + Math.Sign(direction);
        if (index >= 0 && other >= 0 && other < stages.Count)
        {
            (stages[index], stages[other]) = (stages[other], stages[index]);
            for (var i = 0; i < stages.Count; i++) stages[i].Order = (i + 1) * 10;
            await db.SaveChangesAsync(ct);
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken ct)
    {
        if (await db.Tasks.AnyAsync(t => t.StageId == id, ct))
        {
            Error = "No se puede eliminar una columna que tiene tareas (incluidas las archivadas). Muévelas primero.";
            return RedirectToPage();
        }
        if (await db.TaskRuns.AnyAsync(r => r.StageId == id, ct))
        {
            Error = "La columna tiene historial de ejecuciones; en lugar de eliminarla, déjala sin tareas o renómbrala.";
            return RedirectToPage();
        }
        var stage = await db.Stages.FindAsync([id], ct);
        if (stage is not null)
        {
            db.Stages.Remove(stage);
            await db.SaveChangesAsync(ct);
        }
        return RedirectToPage();
    }
}
