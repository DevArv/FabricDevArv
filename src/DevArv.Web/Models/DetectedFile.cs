using DevArv.Web.Enums.Tasks;

namespace DevArv.Web.Models;

public sealed record DetectedFile(AttachmentKind Kind, string ContentType, string Extension);
