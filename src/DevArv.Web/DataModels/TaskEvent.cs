namespace DevArv.Web.DataModels;

/// <summary>Historial: "agents-vm tomó el pedido «Desarrollador»", "Juan aprobó", etc.</summary>
public class TaskEvent
{
    public long Id { get; set; }
    public int TaskId { get; set; }
    public TaskItem Task { get; set; } = null!;
    public string Actor { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
