using DevArv.Web.Enums.Tasks;

namespace DevArv.Web.ViewModels;

/// <summary>Campos editables de una tarea desde la pantalla de detalle.</summary>
public sealed record TaskUpdateViewModel(
    Priority Priority,
    Complexity Complexity,
    string? Labels,
    string? ModelOverride,
    int? PreferredWorkerID,
    string? Title,
    string? Description);
