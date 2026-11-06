using DevArv.Web.DataModels;
using DevArv.Web.Repositories;
using DevArv.Web.ViewModels;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;

namespace DevArv.Web.Pages.Workflow;

public sealed class EditModel(StagesRepository StagesRepository) : PageModel
{
    [BindProperty(SupportsGet = true)] public int? Id { get; set; }
    [BindProperty] public StageEditViewModel Input { get; set; } = new();
    public List<Stage> OtherStages { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken CT)
    {
        OtherStages = await StagesRepository.GetOthersAsync(
            ExcludedID: Id,
            CT: CT);
        if (Id is null)
        {
            Input = StagesRepository.NewEdit();
            return Page();
        }

        var record = await StagesRepository.GetEditAsync(
            ID: Id.Value,
            CT: CT);
        if (record is null) return NotFound();
        Input = record;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken CT)
    {
        OtherStages = await StagesRepository.GetOthersAsync(
            ExcludedID: Id,
            CT: CT);
        if (!ModelState.IsValid) return Page();

        try
        {
            var found = await StagesRepository.SaveAsync(
                ID: Id,
                Record: Input,
                CT: CT);
            if (!found) return NotFound();
        }
        catch (ValidationFailedException ex)
        {
            foreach (var (field, message) in ex.Errors) ModelState.AddModelError(field, message);
            return Page();
        }
        return RedirectToPage("/Workflow/Index");
    }
}
