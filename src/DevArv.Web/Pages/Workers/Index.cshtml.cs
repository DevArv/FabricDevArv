using DevArv.Web.DataModels;
using DevArv.Web.Repositories;
using DevArv.Web.ViewModels;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;

namespace DevArv.Web.Pages.Workers;

public sealed class IndexModel(WorkersRepository workers) : PageModel
{
    public List<WorkerNode> Workers { get; private set; } = [];
    public Dictionary<int, string> CurrentRuns { get; private set; } = [];

    [BindProperty] public WorkerCreateViewModel Input { get; set; } = new();

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
        try
        {
            ShowKey(await workers.InsertAsync(Input, ct));
        }
        catch (ValidationFailedException ex)
        {
            Error = ex.Message;
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRegenerateAsync(int id, CancellationToken ct)
    {
        var result = await workers.RegenerateKeyAsync(id, ct);
        if (result is null) return NotFound();
        ShowKey(result);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleAsync(int id, CancellationToken ct)
    {
        if (!await workers.ToggleEnabledAsync(id, ct)) return NotFound();
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken ct)
    {
        try
        {
            await workers.DeleteAsync(id, ct);
        }
        catch (ValidationFailedException ex)
        {
            Error = ex.Message;
        }
        return RedirectToPage();
    }

    private void ShowKey(WorkerKeyViewModel result)
    {
        NewKeyWorker = result.WorkerName;
        NewKey = result.Key;
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        Workers = await workers.GetAllAsync(ct);
        CurrentRuns = await workers.GetCurrentRunsAsync(ct);
    }
}
