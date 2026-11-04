using DevArv.Web.DataModels;
using DevArv.Web.Models;
using DevArv.Web.Repositories;
using DevArv.Web.Services;
using DevArv.Web.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;

namespace DevArv.Web.Pages.Tasks;

public sealed class CreateModel(
    ProjectsRepository projects, WorkersRepository workers, TaskAttachmentsRepository attachments,
    WorkflowService workflow, UserManager<IdentityUser> users) : PageModel
{
    [BindProperty] public TaskCreateViewModel Input { get; set; } = new();
    [BindProperty] public List<IFormFile> Files { get; set; } = [];
    [BindProperty(SupportsGet = true)] public bool Modal { get; set; }

    public List<Project> Projects { get; private set; } = [];
    public List<WorkerNode> Workers { get; private set; } = [];
    public AttachmentOptions AttachmentLimits => attachments.Limits;

    public async Task<IActionResult> OnGetAsync(int? projectId, CancellationToken ct)
    {
        await LoadListsAsync(ct);
        if (Projects.Count == 0) return Modal ? Content("<p class='empty'>Primero crea un proyecto en «Proyectos».</p>", "text/html") : RedirectToPage("/Projects/Edit");

        var project = Projects.FirstOrDefault(p => p.Id == projectId) ?? Projects[0];
        Input.ProjectId = project.Id;
        Input.BaseBranch = project.DefaultBranch;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        await LoadListsAsync(ct);
        if (!ModelState.IsValid) return Page();

        try
        {
            var task = await workflow.CreateTaskAsync(Input, Files, User.Identity?.Name ?? "?", users.GetUserId(User), ct);
            return Redirect($"/?task={task.Id}");
        }
        catch (ValidationFailedException ex)
        {
            foreach (var (field, message) in ex.Errors) ModelState.AddModelError(field, message);
            return Page();
        }
    }

    private async Task LoadListsAsync(CancellationToken ct)
    {
        Projects = await projects.GetActiveAsync(ct);
        Workers = await workers.GetEnabledAsync(ct);
    }
}
