using DevArv.Web.DataModels;
using DevArv.Web.Repositories;
using DevArv.Web.ViewModels;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;

namespace DevArv.Web.Pages.Workflow;

public sealed class EditModel(StagesRepository StagesRepository) : PageModel
{
    // Estado para la vista; los handlers lo asignan con lo que reciben de la URL y del formulario.
    public int? ID { get; private set; }
    public StageEditViewModel Input { get; private set; } = new();
    public List<Stage> OtherStages { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int? ID, CancellationToken CT)
    {
        this.ID = ID;
        OtherStages = await StagesRepository.GetOthersAsync(
            ExcludedID: ID,
            CT: CT);
        if (ID is null)
        {
            Input = StagesRepository.NewEdit();
            return Page();
        }

        var record = await StagesRepository.GetEditAsync(
            ID: ID.Value,
            CT: CT);
        if (record is null) return NotFound();
        Input = record;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? ID, StageEditViewModel Input, CancellationToken CT)
    {
        this.ID = ID;
        this.Input = Input;
        OtherStages = await StagesRepository.GetOthersAsync(
            ExcludedID: ID,
            CT: CT);
        if (!ModelState.IsValid) return Page();

        try
        {
            var found = await StagesRepository.SaveAsync(
                ID: ID,
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
