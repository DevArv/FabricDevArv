namespace DevArv.Web.DataModels;

public class Project
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Color en hexadecimal para el chip del proyecto (#22c55e).</summary>
    public string Color { get; set; } = "#3b82f6";
    public string RepoOwner { get; set; } = "";
    public string RepoName { get; set; } = "";
    public string DefaultBranch { get; set; } = "main";
    /// <summary>Prefijo de las ramas de trabajo, p. ej. "devarv/" → devarv/12-mi-tarea.</summary>
    public string BranchPrefix { get; set; } = "devarv/";
    /// <summary>Instrucciones extra para todos los agentes de este proyecto (se agregan al system prompt).</summary>
    public string? AgentInstructions { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public string RepoFullName => $"{RepoOwner}/{RepoName}";
}
