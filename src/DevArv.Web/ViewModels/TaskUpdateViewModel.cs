using DevArv.Web.Enums.Tasks;

namespace DevArv.Web.ViewModels;

/// <summary>Campos editables de una tarea desde la pantalla de detalle.</summary>
public sealed record TaskUpdateViewModel(
    Priority Priority,
    Complexity Complexity,
    string? Labels,
    string? ModelOverride,
    int? PreferredWorkerId,
    string? Title,
    string? Description);
