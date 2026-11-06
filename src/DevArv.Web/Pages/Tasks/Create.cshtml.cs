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
    ProjectsRepository ProjectsRepository, WorkersRepository WorkersRepository, TasksRepository TasksRepository,
    TaskAttachmentsRepository AttachmentsRepository, WorkflowService WorkflowService,
    UserManager<IdentityUser> UserManager) : PageModel
{
    [BindProperty] public TaskCreateViewModel Input { get; set; } = new();
    [BindProperty] public List<IFormFile> Files { get; set; } = [];
    [BindProperty(SupportsGet = true)] public bool Modal { get; set; }

    public List<Project> Projects { get; private set; } = [];
    public List<WorkerNode> Workers { get; private set; } = [];
    public AttachmentOptions AttachmentLimits => AttachmentsRepository.Limits;

    public async Task<IActionResult> OnGetAsync(int? ProjectID, CancellationToken CT)
    {
        var record = await TasksRepository.NewCreateAsync(
            ProjectID: ProjectID,
            CT: CT);
        if (record is null) return Modal ? Content("<p class='empty'>Primero crea un proyecto en «Proyectos».</p>", "text/html") : RedirectToPage("/Projects/Edit");

        await LoadListsAsync(CT);
        Input = record;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken CT)
    {
        await LoadListsAsync(CT);
        if (!ModelState.IsValid) return Page();

        try
        {
            var task = await WorkflowService.CreateTaskAsync(
                Record: Input,
                Files: Files,
                Actor: User.Identity?.Name ?? "?",
                UserID: UserManager.GetUserId(User),
                CT: CT);
            return Redirect($"/?task={task.Id}");
        }
        catch (ValidationFailedException ex)
        {
            foreach (var (field, message) in ex.Errors) ModelState.AddModelError(field, message);
            return Page();
        }
    }

    private async Task LoadListsAsync(CancellationToken CT)
    {
        Projects = await ProjectsRepository.GetActiveAsync(CT);
        Workers = await WorkersRepository.GetEnabledAsync(CT);
    }
}
