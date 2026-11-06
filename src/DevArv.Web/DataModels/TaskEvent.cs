namespace DevArv.Web.DataModels;

/// <summary>Historial: "agents-vm tomó el pedido «Desarrollador»", "Juan aprobó", etc.</summary>
public class TaskEvent
{
    public long ID { get; set; }
    public int TaskID { get; set; }
    public TaskItem Task { get; set; } = null!;
    public string Actor { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
