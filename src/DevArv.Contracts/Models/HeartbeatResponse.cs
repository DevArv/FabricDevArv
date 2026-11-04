namespace DevArv.Contracts.Models;

public sealed record HeartbeatResponse(DateTimeOffset ServerTime, string WorkerName);
