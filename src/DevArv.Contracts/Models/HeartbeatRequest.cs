namespace DevArv.Contracts.Models;

public sealed record HeartbeatRequest(string MachineName, string Version, string? OsDescription, int RunningJobs);
