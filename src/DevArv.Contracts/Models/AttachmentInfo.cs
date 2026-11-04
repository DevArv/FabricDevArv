namespace DevArv.Contracts.Models;

public sealed record AttachmentInfo(long Id, string FileName, string ContentType, long Size)
{
    /// <summary>Nombre con el que el worker guarda el adjunto en disco (el prompt lo referencia igual).</summary>
    public string LocalFileName => $"{Id}-{FileName}";
}
