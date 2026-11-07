using DevArv.Web.Api;
using DevArv.Web.DataModels;
using DevArv.Web.Enums.Tasks;

namespace DevArv.Tests.Web.Api;

public class WorkerApiTests
{
    private static TaskAttachment Attachment(long ID, string FileName, bool IsCapture) => new()
    {
        ID = ID,
        FileName = FileName,
        ContentType = "image/png",
        Kind = AttachmentKind.IMAGE,
        Size = 10,
        IsCapture = IsCapture,
    };

    [Fact]
    public void JobAttachments_ExcludesCaptures_AndOrdersByID()
    {
        var task = new TaskItem
        {
            Attachments = [Attachment(3, "c.png", false), Attachment(2, "captura.png", true), Attachment(1, "a.png", false)],
        };

        var result = WorkerApi.JobAttachments(task);

        Assert.Equal([1L, 3L], result.Select(a => a.ID));
    }

    [Fact]
    public void JobAttachments_WithOnlyCaptures_IsEmpty()
    {
        var task = new TaskItem { Attachments = [Attachment(1, "captura.png", true)] };

        Assert.Empty(WorkerApi.JobAttachments(task));
    }
}
