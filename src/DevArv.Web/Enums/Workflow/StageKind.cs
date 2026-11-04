namespace DevArv.Web.Enums.Workflow;

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
