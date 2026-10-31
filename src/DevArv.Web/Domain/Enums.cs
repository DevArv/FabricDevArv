namespace DevArv.Web.Domain;

/// <summary>Qué tipo de trabajo ocurre en una columna del tablero.</summary>
public enum StageKind
{
    /// <summary>Columna de espera; una persona mueve la tarea manualmente.</summary>
    Manual = 0,
    /// <summary>Requiere que una persona apruebe o pida cambios.</summary>
    HumanApproval = 1,
    /// <summary>Un agente de Claude Code ejecuta la etapa en un worker (PC).</summary>
    Agent = 2,
    /// <summary>El servidor ejecuta una acción automática (crear PR, merge).</summary>
    System = 3,
    /// <summary>Estado final (terminado o descartado).</summary>
    Terminal = 4,
}

public enum SystemAction
{
    None = 0,
    CreatePullRequest = 1,
    MergePullRequest = 2,
}

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

public enum Priority
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3,
}

public enum Complexity
{
    Unclassified = 0,
    Low = 1,
    Medium = 2,
    High = 3,
}

public enum RunStatus
{
    Queued = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
    Cancelled = 4,
}

public enum RunDecision
{
    None = 0,
    Approved = 1,
    Rejected = 2,
}

public enum CommentKind
{
    Comment = 0,
    /// <summary>Cambios pedidos por una persona; se envían al agente en la siguiente ejecución.</summary>
    Feedback = 1,
    /// <summary>Hallazgos de un agente revisor que rechazó la tarea.</summary>
    ReviewerNote = 2,
}

public enum AttachmentKind
{
    Image = 0,
    Pdf = 1,
    Html = 2,
}
