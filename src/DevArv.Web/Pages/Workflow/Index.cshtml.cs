using DevArv.Web.DataModels;
using DevArv.Web.Repositories;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;

namespace DevArv.Web.Pages.Workflow;

public sealed class IndexModel(StagesRepository StagesRepository) : PageModel
{
    public List<Stage> Stages { get; private set; } = [];
    public Dictionary<int, int> TaskCounts { get; private set; } = [];
    [TempData] public string? Error { get; set; }

    public async Task OnGetAsync(CancellationToken CT)
    {
        Stages = await StagesRepository.GetAllAsync(CT);
        TaskCounts = await StagesRepository.GetTaskCountsAsync(CT);
    }

    public string NameOf(int? ID) => Stages.FirstOrDefault(s => s.ID == ID)?.Name ?? "—";

    public async Task<IActionResult> OnPostMoveAsync(int ID, int Direction, CancellationToken CT)
    {
        await StagesRepository.MoveAsync(
            ID: ID,
            Direction: Direction,
            CT: CT);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int ID, CancellationToken CT)
    {
        try
        {
            await StagesRepository.DeleteAsync(
                ID: ID,
                CT: CT);
        }
        catch (ValidationFailedException ex)
        {
            Error = ex.Message;
        }
        return RedirectToPage();
    }
}
