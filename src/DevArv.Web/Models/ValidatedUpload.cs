namespace DevArv.Web.Models;

public sealed record ValidatedUpload(IFormFile File, DetectedFile Type);
