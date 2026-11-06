namespace DevArv.Web.DataModels;

/// <summary>Una PC registrada que ejecuta el worker de agentes.</summary>
public class WorkerNode
{
    public int ID { get; set; }
    public string Name { get; set; } = "";
    /// <summary>SHA-256 (hex) de la API key. La key en claro sólo se muestra al crearla.</summary>
    public string KeyHash { get; set; } = "";
    public string KeyPrefix { get; set; } = "";
    public string? MachineName { get; set; }
    public string? Version { get; set; }
    public string? OsDescription { get; set; }
    public int RunningJobs { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSeenAt { get; set; }

    public bool IsOnline(DateTimeOffset Now) => LastSeenAt is { } seen && Now - seen < TimeSpan.FromMinutes(2);
}
