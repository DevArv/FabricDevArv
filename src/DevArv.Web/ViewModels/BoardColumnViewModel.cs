using DevArv.Web.DataModels;

namespace DevArv.Web.ViewModels;

public sealed class BoardColumnViewModel
{
    public required Stage Stage { get; init; }
    public required List<BoardCardViewModel> Tasks { get; init; }
    public string? SuccessName { get; init; }
    public string? RejectName { get; init; }
}
