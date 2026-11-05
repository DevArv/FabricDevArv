using System.ComponentModel.DataAnnotations;
using DevArv.Web.Enums.Workflow;

namespace DevArv.Web.ViewModels;

public sealed class StageEditViewModel
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
    [MaxLength(100), RegularExpression(@"^/?[A-Za-z0-9][A-Za-z0-9:._\-]*$",
         ErrorMessage = "Nombre de skill inválido: usa letras, números, guiones o «plugin:skill», sin espacios.")]
    public string? Skill { get; set; }
    public bool PushChanges { get; set; }
    public bool RequiresDecision { get; set; }
    [Range(1, 600)] public int TimeoutMinutes { get; set; } = 60;
    public SystemAction SystemAction { get; set; }
}
