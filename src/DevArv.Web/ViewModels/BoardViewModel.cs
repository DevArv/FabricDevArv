namespace DevArv.Web.ViewModels;

public sealed class BoardViewModel
{
    public List<BoardColumnViewModel> Columns { get; init; } = [];
    public bool Compact { get; init; }
    public bool HideEmpty { get; init; }
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;
}
