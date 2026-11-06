using DevArv.Web.Repositories;
using DevArv.Web.Services;
using DevArv.Web.ViewModels;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;

namespace DevArv.Web.Pages.Projects;

public sealed class EditModel(ProjectsRepository ProjectsRepository, GitHubService GitHubService) : PageModel
{
    [BindProperty(SupportsGet = true)] public int? Id { get; set; }
    [BindProperty] public ProjectEditViewModel Input { get; set; } = new();
    public string? Message { get; private set; }
    public bool MessageIsError { get; private set; }
    public bool GitHubConfigured => GitHubService.IsConfigured;

    public async Task<IActionResult> OnGetAsync(CancellationToken CT)
    {
        if (Id is null) return Page();
        var record = await ProjectsRepository.GetEditAsync(
            ID: Id.Value,
            CT: CT);
        if (record is null) return NotFound();
        Input = record;
        return Page();
    }

    public async Task<IActionResult> OnPostTestAsync(CancellationToken CT)
    {
        ModelState.Clear();
        var result = await ProjectsRepository.TestConnectionAsync(
            Record: Input,
            CT: CT);
        Message = result.Message;
        MessageIsError = result.IsError;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken CT)
    {
        if (!ModelState.IsValid) return Page();

        try
        {
            var found = await ProjectsRepository.SaveAsync(
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
        return RedirectToPage("/Projects/Index");
    }
}
