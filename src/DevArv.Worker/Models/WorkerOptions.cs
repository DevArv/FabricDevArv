namespace DevArv.Worker.Models;

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
