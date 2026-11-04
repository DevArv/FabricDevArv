using System.ComponentModel.DataAnnotations;

namespace DevArv.Web.ViewModels;

public sealed class ProjectEditViewModel
{
    [Required(ErrorMessage = "El nombre es obligatorio."), MaxLength(120)]
    public string Name { get; set; } = "";

    [RegularExpression("^#[0-9a-fA-F]{6}$", ErrorMessage = "Color inválido.")]
    public string Color { get; set; } = "#3b82f6";

    /// <summary>Acepta "owner/repo" o la URL completa de GitHub.</summary>
    [Required(ErrorMessage = "Indica el repositorio (owner/repo o URL).")]
    public string Repository { get; set; } = "";

    [Required, MaxLength(200)]
    [RegularExpression(@"^[A-Za-z0-9._/\-]+$", ErrorMessage = "Nombre de rama inválido.")]
    public string DefaultBranch { get; set; } = "main";

    [Required, MaxLength(60)]
    [RegularExpression(@"^[A-Za-z0-9._/\-]*$", ErrorMessage = "Prefijo inválido.")]
    public string BranchPrefix { get; set; } = "devarv/";

    [MaxLength(20_000)]
    public string? AgentInstructions { get; set; }

    public bool IsActive { get; set; } = true;
}
