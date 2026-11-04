namespace DevArv.Worker.Models;

public sealed class WorkspaceOptions
{
    /// <summary>Carpeta donde se clonan los repositorios y se crean los worktrees por tarea.</summary>
    public string Root { get; set; } = "";

    public string ResolveRoot()
    {
        var root = string.IsNullOrWhiteSpace(Root)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "devarv-workspace")
            : Environment.ExpandEnvironmentVariables(Root);
        return Path.GetFullPath(root);
    }
}
