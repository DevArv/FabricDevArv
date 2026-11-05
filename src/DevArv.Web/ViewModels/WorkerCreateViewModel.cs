using System.ComponentModel.DataAnnotations;

namespace DevArv.Web.ViewModels;

public sealed class WorkerCreateViewModel
{
    [Required(ErrorMessage = "Escribe un nombre."), MaxLength(100)]
    [RegularExpression(@"^[A-Za-z0-9._\-]+$", ErrorMessage = "Usa letras, números, punto, guion o guion bajo.")]
    public string Name { get; set; } = "";
}
