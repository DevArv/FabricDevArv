using System.ComponentModel.DataAnnotations;
using DevArv.Web.Data;
using DevArv.Web.Domain;
using DevArv.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace DevArv.Web.Pages.Tasks;

public sealed class CreateModel(
    AppDbContext db, WorkflowService workflow, AttachmentStorage storage, UserManager<IdentityUser> users) : PageModel
{
    public sealed class InputModel
    {
        [Required(ErrorMessage = "El título es obligatorio."), MaxLength(200)]
        [Display(Name = "Título")]
        public string Title { get; set; } = "";

        [Display(Name = "Proyecto")]
        public int ProjectId { get; set; }

        [Required(ErrorMessage = "Indica la rama base."), MaxLength(200)]
        [RegularExpression(@"^[A-Za-z0-9._/\-]+$", ErrorMessage = "Nombre de rama inválido.")]
        [Display(Name = "Rama base")]
        public string BaseBranch { get; set; } = "main";

        public Priority Priority { get; set; } = Priority.Medium;
        public Complexity Complexity { get; set; }

        [MaxLength(500)]
        [Display(Name = "Etiquetas")]
        public string? Labels { get; set; }

        [MaxLength(50_000)]
        [Display(Name = "Descripción")]
        public string Description { get; set; } = "";

        [MaxLength(100)]
        [RegularExpression(@"^[A-Za-z0-9.\-_\[\]]*$", ErrorMessage = "Nombre de modelo inválido.")]
        public string? ModelOverride { get; set; }

        public int? PreferredWorkerId { get; set; }

        /// <summary>Si es true la tarea entra directo a la primera columna de desarrollo (agente).</summary>
        public bool StartNow { get; set; }
    }

    [BindProperty] public InputModel Input { get; set; } = new();
    [BindProperty] public List<IFormFile> Files { get; set; } = [];
    [BindProperty(SupportsGet = true)] public bool Modal { get; set; }

    public List<Project> Projects { get; private set; } = [];
    public List<WorkerNode> Workers { get; private set; } = [];
    public AttachmentOptions AttachmentLimits => storage.Options;

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
        var project = Projects.FirstOrDefault(p => p.Id == Input.ProjectId);
        if (project is null) ModelState.AddModelError("Input.ProjectId", "Selecciona un proyecto activo.");

        List<ValidatedUpload> uploads = [];
        try
        {
            uploads = await storage.ValidateAsync(Files, 0, ct);
        }
        catch (AttachmentValidationException ex)
        {
            ModelState.AddModelError(nameof(Files), ex.Message);
        }

        if (!ModelState.IsValid || project is null) return Page();

        var actor = User.Identity?.Name ?? "?";
        var initial = await workflow.GetInitialStageAsync(ct);
        var task = new TaskItem
        {
            Title = Input.Title.Trim(),
            Description = Input.Description?.Trim() ?? "",
            ProjectId = project.Id,
            StageId = initial.Id,
            BaseBranch = Input.BaseBranch.Trim(),
            Priority = Input.Priority,
            Complexity = Input.Complexity,
            Labels = NormalizeLabels(Input.Labels),
            ModelOverride = string.IsNullOrWhiteSpace(Input.ModelOverride) ? null : Input.ModelOverride.Trim(),
            PreferredWorkerId = Workers.Any(w => w.Id == Input.PreferredWorkerId) ? Input.PreferredWorkerId : null,
            CreatedById = users.GetUserId(User),
            CreatedByName = actor,
        };
        db.Tasks.Add(task);
        await db.SaveChangesAsync(ct);

        db.TaskAttachments.AddRange(await storage.StoreAsync(task.Id, uploads, ct));

        // Project se cargó sin seguimiento: se adjunta para que EnterStage pueda leer BranchPrefix.
        task.Project = await db.Projects.FirstAsync(p => p.Id == project.Id, ct);
        var start = initial;
        if (Input.StartNow)
        {
            start = await db.Stages.Where(s => s.Kind == StageKind.Agent && s.PushChanges)
                .OrderBy(s => s.Order).FirstOrDefaultAsync(ct) ?? initial;
        }
        await workflow.EnterStageAsync(task, start, actor, $"{actor} creó la tarea en «{start.Name}»", ct);
        await db.SaveChangesAsync(ct);

        return Redirect($"/?task={task.Id}");
    }

    private async Task LoadListsAsync(CancellationToken ct)
    {
        Projects = await db.Projects.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.Name).ToListAsync(ct);
        Workers = await db.Workers.AsNoTracking().Where(w => w.IsEnabled).OrderBy(w => w.Name).ToListAsync(ct);
    }

    internal static string? NormalizeLabels(string? labels)
    {
        if (string.IsNullOrWhiteSpace(labels)) return null;
        var parts = labels.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var joined = string.Join(", ", parts);
        return joined.Length == 0 ? null : joined[..Math.Min(joined.Length, 500)];
    }
}
