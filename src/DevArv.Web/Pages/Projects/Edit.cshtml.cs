using DevArv.Web.Repositories;
using DevArv.Web.Services;
using DevArv.Web.ViewModels;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;

namespace DevArv.Web.Pages.Projects;

public sealed class EditModel(ProjectsRepository ProjectsRepository, GitHubService GitHubService) : PageModel
{
    // Estado para la vista; los handlers lo asignan con lo que reciben de la URL y del formulario.
    public int? ID { get; private set; }
    public ProjectEditViewModel Input { get; private set; } = new();
    public string? Message { get; private set; }
    public bool MessageIsError { get; private set; }
    public bool GitHubConfigured => GitHubService.IsConfigured;

    public async Task<IActionResult> OnGetAsync(int? ID, CancellationToken CT)
    {
        this.ID = ID;
        if (ID is null) return Page();
        var record = await ProjectsRepository.GetEditAsync(
            ID: ID.Value,
            CT: CT);
        if (record is null) return NotFound();
        Input = record;
        return Page();
    }

    public async Task<IActionResult> OnPostTestAsync(int? ID, ProjectEditViewModel Input, CancellationToken CT)
    {
        this.ID = ID;
        this.Input = Input;
        ModelState.Clear();
        var result = await ProjectsRepository.TestConnectionAsync(
            Record: Input,
            CT: CT);
        Message = result.Message;
        MessageIsError = result.IsError;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? ID, ProjectEditViewModel Input, CancellationToken CT)
    {
        this.ID = ID;
        this.Input = Input;
        if (!ModelState.IsValid) return Page();

        try
        {
            var found = await ProjectsRepository.SaveAsync(
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
        return RedirectToPage("/Projects/Index");
    }
}
