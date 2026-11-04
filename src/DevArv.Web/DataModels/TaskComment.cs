using DevArv.Web.Enums.Tasks;

namespace DevArv.Web.DataModels;

public class TaskComment
{
    public long Id { get; set; }
    public int TaskId { get; set; }
    public TaskItem Task { get; set; } = null!;
    public CommentKind Kind { get; set; }
    public string Author { get; set; } = "";
    public string Body { get; set; } = "";
    public long? RunId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
