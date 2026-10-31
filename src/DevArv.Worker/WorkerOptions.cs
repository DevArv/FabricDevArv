namespace DevArv.Worker;

public sealed class WorkerOptions
{
    public ServerOptions Server { get; set; } = new();
    public WorkspaceOptions Workspace { get; set; } = new();
    public ClaudeOptions Claude { get; set; } = new();
    public GitOptions Git { get; set; } = new();

    /// <summary>Cuántas tareas puede ejecutar esta PC al mismo tiempo.</summary>
    public int MaxParallelJobs { get; set; } = 1;

    /// <summary>Segundos entre consultas al servidor cuando no hay trabajo.</summary>
    public int PollSeconds { get; set; } = 5;
}

public sealed class ServerOptions
{
    /// <summary>URL del servidor DevArv, p. ej. https://devarv.miempresa.com</summary>
    public string Url { get; set; } = "";

    /// <summary>Clave generada en «Mis PCs».</summary>
    public string ApiKey { get; set; } = "";
}

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

public sealed class ClaudeOptions
{
    /// <summary>Ejecutable de Claude Code. "claude" lo busca en el PATH.</summary>
    public string Executable { get; set; } = "claude";

    /// <summary>Opcional. Si se define se pasa como ANTHROPIC_API_KEY; si no, se usa la sesión iniciada con `claude login`.</summary>
    public string? AnthropicApiKey { get; set; }

    /// <summary>Variables de entorno adicionales para el proceso de Claude.</summary>
    public Dictionary<string, string> Environment { get; set; } = new();
}

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
