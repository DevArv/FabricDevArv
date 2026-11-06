using System.ComponentModel.DataAnnotations;
using DevArv.Web.Enums.Tasks;

namespace DevArv.Web.ViewModels;

public sealed class TaskCreateViewModel
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
    [RegularExpression(@"^[A-Za-z0-9][A-Za-z0-9.\-_\[\]]*$", ErrorMessage = "Nombre de modelo inválido.")]
    public string? ModelOverride { get; set; }

    public int? PreferredWorkerId { get; set; }

    /// <summary>Si es true la tarea entra directo a la primera columna de desarrollo (agente).</summary>
    public bool StartNow { get; set; }
}
