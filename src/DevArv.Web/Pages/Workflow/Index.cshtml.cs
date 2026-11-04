using DevArv.Web.DataModels;
using DevArv.Web.Repositories;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;

namespace DevArv.Web.Pages.Workflow;

public sealed class IndexModel(StagesRepository stages) : PageModel
{
    public List<Stage> Stages { get; private set; } = [];
    public Dictionary<int, int> TaskCounts { get; private set; } = [];
    [TempData] public string? Error { get; set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        Stages = await stages.GetAllAsync(ct);
        TaskCounts = await stages.GetTaskCountsAsync(ct);
    }

    public string NameOf(int? id) => Stages.FirstOrDefault(s => s.Id == id)?.Name ?? "—";

    public async Task<IActionResult> OnPostMoveAsync(int id, int direction, CancellationToken ct)
    {
        await stages.MoveAsync(id, direction, ct);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken ct)
    {
        try
        {
            await stages.DeleteAsync(id, ct);
        }
        catch (ValidationFailedException ex)
        {
            Error = ex.Message;
        }
        return RedirectToPage();
    }
}
