namespace DevArv.Web.Enums.Tasks;

/// <summary>Estado operativo de la tarea dentro de su columna actual.</summary>
public enum WorkStatus
{
    IDLE = 0,
    QUEUED = 1,
    RUNNING = 2,
    WAITING_APPROVAL = 3,
    FAILED = 4,
    DONE = 5,
}
