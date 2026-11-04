namespace DevArv.Web.Enums.Tasks;

/// <summary>Estado operativo de la tarea dentro de su columna actual.</summary>
public enum WorkStatus
{
    Idle = 0,
    Queued = 1,
    Running = 2,
    WaitingApproval = 3,
    Failed = 4,
    Done = 5,
}
