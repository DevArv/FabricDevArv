using DevArv.Web.Repositories;
using DevArv.Web.Services;
using DevArv.Web.ViewModels;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;

namespace DevArv.Web.Pages.Projects;

public sealed class EditModel(ProjectsRepository projects, GitHubService github) : PageModel
{
    [BindProperty(SupportsGet = true)] public int? Id { get; set; }
    [BindProperty] public ProjectEditViewModel Input { get; set; } = new();
    public string? Message { get; private set; }
    public bool MessageIsError { get; private set; }
    public bool GitHubConfigured => github.IsConfigured;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (Id is null) return Page();
        var record = await projects.GetEditAsync(Id.Value, ct);
        if (record is null) return NotFound();
        Input = record;
        return Page();
    }

    public async Task<IActionResult> OnPostTestAsync(CancellationToken ct)
    {
        ModelState.Clear();
        var result = await projects.TestConnectionAsync(Input, ct);
        Message = result.Message;
        MessageIsError = result.IsError;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        try
        {
            projects.Validate(Input);
        }
        catch (ValidationFailedException ex)
        {
            AddErrors(ex);
        }
        if (!ModelState.IsValid) return Page();

        try
        {
            if (!await projects.SaveAsync(Id, Input, ct)) return NotFound();
        }
        catch (ValidationFailedException ex)
        {
            AddErrors(ex);
            return Page();
        }
        return RedirectToPage("/Projects/Index");
    }

    private void AddErrors(ValidationFailedException ex)
    {
        foreach (var (field, message) in ex.Errors) ModelState.AddModelError(field, message);
    }
}
