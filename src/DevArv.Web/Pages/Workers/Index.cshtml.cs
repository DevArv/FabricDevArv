using DevArv.Web.DataModels;
using DevArv.Web.Repositories;
using DevArv.Web.ViewModels;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;

namespace DevArv.Web.Pages.Workers;

public sealed class IndexModel(WorkersRepository WorkersRepository) : PageModel
{
    public List<WorkerNode> Workers { get; private set; } = [];
    public Dictionary<int, string> CurrentRuns { get; private set; } = [];

    // Estado para la vista; el handler de alta lo asigna con lo que recibe del formulario.
    public WorkerCreateViewModel Input { get; private set; } = new();

    [TempData] public string? NewKey { get; set; }
    [TempData] public string? NewKeyWorker { get; set; }
    [TempData] public string? Error { get; set; }

    public async Task OnGetAsync(CancellationToken CT) => await LoadAsync(CT);

    public async Task<IActionResult> OnPostCreateAsync(WorkerCreateViewModel Input, CancellationToken CT)
    {
        this.Input = Input;
        if (!ModelState.IsValid)
        {
            await LoadAsync(CT);
            return Page();
        }
        try
        {
            ShowKey(await WorkersRepository.InsertAsync(
                Record: Input,
                CT: CT));
        }
        catch (ValidationFailedException ex)
        {
            Error = ex.Message;
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRegenerateAsync(int ID, CancellationToken CT)
    {
        var result = await WorkersRepository.RegenerateKeyAsync(
            ID: ID,
            CT: CT);
        if (result is null) return NotFound();
        ShowKey(result);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleAsync(int ID, CancellationToken CT)
    {
        var found = await WorkersRepository.ToggleEnabledAsync(
            ID: ID,
            CT: CT);
        if (!found) return NotFound();
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int ID, CancellationToken CT)
    {
        try
        {
            await WorkersRepository.DeleteAsync(
                ID: ID,
                CT: CT);
        }
        catch (ValidationFailedException ex)
        {
            Error = ex.Message;
        }
        return RedirectToPage();
    }

    private void ShowKey(WorkerKeyViewModel Result)
    {
        NewKeyWorker = Result.WorkerName;
        NewKey = Result.Key;
    }

    private async Task LoadAsync(CancellationToken CT)
    {
        Workers = await WorkersRepository.GetAllAsync(CT);
        CurrentRuns = await WorkersRepository.GetCurrentRunsAsync(CT);
    }
}
