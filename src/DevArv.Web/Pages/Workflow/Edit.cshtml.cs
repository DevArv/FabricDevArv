using DevArv.Web.DataModels;
using DevArv.Web.Repositories;
using DevArv.Web.ViewModels;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;

namespace DevArv.Web.Pages.Workflow;

public sealed class EditModel(StagesRepository stages) : PageModel
{
    [BindProperty(SupportsGet = true)] public int? Id { get; set; }
    [BindProperty] public StageEditViewModel Input { get; set; } = new();
    public List<Stage> OtherStages { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        OtherStages = await stages.GetOthersAsync(Id, ct);
        if (Id is null)
        {
            Input = stages.NewEdit();
            return Page();
        }

        var record = await stages.GetEditAsync(Id.Value, ct);
        if (record is null) return NotFound();
        Input = record;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        OtherStages = await stages.GetOthersAsync(Id, ct);
        foreach (var (field, message) in await stages.GetValidationErrorsAsync(Id, Input, ct))
            ModelState.AddModelError(field, message);
        if (!ModelState.IsValid) return Page();

        if (!await stages.SaveAsync(Id, Input, ct)) return NotFound();
        return RedirectToPage("/Workflow/Index");
    }
}
