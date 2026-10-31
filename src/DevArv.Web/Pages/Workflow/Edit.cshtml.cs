using System.ComponentModel.DataAnnotations;
using DevArv.Web.Data;
using DevArv.Web.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace DevArv.Web.Pages.Workflow;

public sealed class EditModel(AppDbContext db) : PageModel
{
    public static readonly string[] PermissionModes = ["default", "acceptEdits", "plan", "dontAsk", "bypassPermissions"];

    public sealed class InputModel
    {
        [Required(ErrorMessage = "El nombre es obligatorio."), MaxLength(80)] public string Name { get; set; } = "";
        [MaxLength(16)] public string Icon { get; set; } = "📋";
        public StageKind Kind { get; set; } = StageKind.Manual;
        [MaxLength(500)] public string? Description { get; set; }
        public bool IsInitial { get; set; }
        public int? SuccessStageId { get; set; }
        public int? RejectStageId { get; set; }

        [MaxLength(100), RegularExpression(@"^[A-Za-z0-9.\-_\[\]]*$", ErrorMessage = "Nombre de modelo inválido.")]
        public string? Model { get; set; }
        public string PermissionMode { get; set; } = "acceptEdits";
        public string? AllowedTools { get; set; }
        public string? DisallowedTools { get; set; }
        [Range(1, 500)] public int? MaxTurns { get; set; }
        public string? ExtraArgs { get; set; }
        public string? PromptTemplate { get; set; }
        public string? AppendSystemPrompt { get; set; }
        public bool PushChanges { get; set; }
        public bool RequiresDecision { get; set; }
        [Range(1, 600)] public int TimeoutMinutes { get; set; } = 60;
        public SystemAction SystemAction { get; set; }
    }

    [BindProperty(SupportsGet = true)] public int? Id { get; set; }
    [BindProperty] public InputModel Input { get; set; } = new();
    public List<Stage> OtherStages { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        await LoadStagesAsync(ct);
        if (Id is null)
        {
            Input.PromptTemplate = DefaultPrompts.Developer;
            Input.AllowedTools = DefaultPrompts.DeveloperAllowedTools;
            return Page();
        }

        var s = await db.Stages.FindAsync([Id.Value], ct);
        if (s is null) return NotFound();
        Input = new InputModel
        {
            Name = s.Name, Icon = s.Icon, Kind = s.Kind, Description = s.Description, IsInitial = s.IsInitial,
            SuccessStageId = s.SuccessStageId, RejectStageId = s.RejectStageId, Model = s.Model,
            PermissionMode = s.PermissionMode, AllowedTools = s.AllowedTools, DisallowedTools = s.DisallowedTools,
            MaxTurns = s.MaxTurns, ExtraArgs = s.ExtraArgs, PromptTemplate = s.PromptTemplate,
            AppendSystemPrompt = s.AppendSystemPrompt, PushChanges = s.PushChanges,
            RequiresDecision = s.RequiresDecision, TimeoutMinutes = s.TimeoutMinutes, SystemAction = s.SystemAction,
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        await LoadStagesAsync(ct);
        if (!PermissionModes.Contains(Input.PermissionMode))
            ModelState.AddModelError("Input.PermissionMode", "Modo de permisos inválido.");
        if (Input.SuccessStageId is { } ok && !OtherStages.Any(s => s.Id == ok))
            ModelState.AddModelError("Input.SuccessStageId", "Columna destino inválida.");
        if (Input.RejectStageId is { } ko && !OtherStages.Any(s => s.Id == ko))
            ModelState.AddModelError("Input.RejectStageId", "Columna de rechazo inválida.");
        if (Input.Kind == StageKind.System && Input.SystemAction == SystemAction.None)
            ModelState.AddModelError("Input.SystemAction", "Elige la acción de sistema.");
        if (Input.RequiresDecision && Input.RejectStageId is null)
            ModelState.AddModelError("Input.RejectStageId", "Una etapa con veredicto necesita columna de rechazo.");
        var badArg = Lines(Input.ExtraArgs).FirstOrDefault(a => a.Contains(' ') && a.StartsWith("--"));
        if (badArg is not null)
            ModelState.AddModelError("Input.ExtraArgs", $"Escribe un argumento por línea («{badArg}» debe ir en dos líneas).");
        if (!ModelState.IsValid) return Page();

        Stage stage;
        if (Id is null)
        {
            stage = new Stage { Order = (await db.Stages.MaxAsync(s => (int?)s.Order, ct) ?? 0) + 10 };
            db.Stages.Add(stage);
        }
        else
        {
            stage = await db.Stages.FindAsync([Id.Value], ct) ?? throw new InvalidOperationException();
        }

        stage.Name = Input.Name.Trim();
        stage.Icon = string.IsNullOrWhiteSpace(Input.Icon) ? "📋" : Input.Icon.Trim();
        stage.Kind = Input.Kind;
        stage.Description = Input.Description;
        stage.SuccessStageId = Input.SuccessStageId;
        stage.RejectStageId = Input.RejectStageId;
        stage.Model = string.IsNullOrWhiteSpace(Input.Model) ? null : Input.Model.Trim();
        stage.PermissionMode = Input.PermissionMode;
        stage.AllowedTools = Input.AllowedTools;
        stage.DisallowedTools = Input.DisallowedTools;
        stage.MaxTurns = Input.MaxTurns;
        stage.ExtraArgs = Input.ExtraArgs;
        stage.PromptTemplate = Input.PromptTemplate;
        stage.AppendSystemPrompt = Input.AppendSystemPrompt;
        stage.PushChanges = Input.Kind == StageKind.Agent && Input.PushChanges;
        stage.RequiresDecision = Input.Kind == StageKind.Agent && Input.RequiresDecision;
        stage.TimeoutMinutes = Input.TimeoutMinutes;
        stage.SystemAction = Input.Kind == StageKind.System ? Input.SystemAction : SystemAction.None;

        if (Input.IsInitial)
        {
            await db.Stages.Where(s => s.IsInitial).ForEachAsync(s => s.IsInitial = false, ct);
            stage.IsInitial = true;
        }
        else
        {
            stage.IsInitial = false;
        }

        await db.SaveChangesAsync(ct);
        return RedirectToPage("/Workflow/Index");
    }

    private async Task LoadStagesAsync(CancellationToken ct) =>
        OtherStages = await db.Stages.AsNoTracking().Where(s => s.Id != Id).OrderBy(s => s.Order).ToListAsync(ct);

    private static IEnumerable<string> Lines(string? text) =>
        (text ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
