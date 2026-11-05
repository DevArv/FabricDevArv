namespace DevArv.Worker.Models;

public sealed class GitOptions
{
    public string Executable { get; set; } = "git";

    /// <summary>
    /// Obligatorio. Token de GitHub para clonar y hacer push (permiso Contents: Read and write).
    /// Es la única credencial que usa git: se desactivan los credential helpers de la PC (Git Credential Manager, etc.).
    /// </summary>
    public string? GitHubToken { get; set; }

    public string AuthorName { get; set; } = "DevArv Agent";
    public string AuthorEmail { get; set; } = "devarv-agent@users.noreply.github.com";
}
