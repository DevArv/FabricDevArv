using System.ComponentModel.DataAnnotations;
using DevArv.Web.DataModels;
using DevArv.Web.Repositories;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;

namespace DevArv.Web.Pages.Workers;

public sealed class IndexModel(WorkersRepository workers) : PageModel
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
        try
        {
            NewKey = await workers.InsertAsync(NewName, ct);
            NewKeyWorker = NewName.Trim();
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
        (NewKeyWorker, NewKey) = result.Value;
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

    private async Task LoadAsync(CancellationToken ct)
    {
        Workers = await workers.GetAllAsync(ct);
        CurrentRuns = await workers.GetCurrentRunsAsync(ct);
    }
}
