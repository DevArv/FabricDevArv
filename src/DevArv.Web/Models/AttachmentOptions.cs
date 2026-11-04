namespace DevArv.Web.Models;

public sealed class AttachmentOptions
{
    public string StoragePath { get; set; } = "App_Data/attachments";
    public int MaxFilesPerTask { get; set; } = 15;
    public long MaxFileBytes { get; set; } = 20 * 1024 * 1024;
}
