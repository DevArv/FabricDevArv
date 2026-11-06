using DevArv.Web.Enums.Tasks;

namespace DevArv.Web.DataModels;

public class TaskComment
{
    public long ID { get; set; }
    public int TaskID { get; set; }
    public TaskItem Task { get; set; } = null!;
    public CommentKind Kind { get; set; }
    public string Author { get; set; } = "";
    public string Body { get; set; } = "";
    public long? RunID { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
