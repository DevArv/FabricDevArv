namespace DevArv.Worker.Models;

public sealed class GitOptions
{
    public string Executable { get; set; } = "git";

    /// <summary>
    /// Token de GitHub para clonar y hacer push (permiso Contents: Read and write).
    /// Si se deja vacío se usan las credenciales que ya tenga git en la PC (Git Credential Manager, SSH, etc.).
    /// </summary>
    public string? GitHubToken { get; set; }

    public string AuthorName { get; set; } = "DevArv Agent";
    public string AuthorEmail { get; set; } = "devarv-agent@users.noreply.github.com";
}
