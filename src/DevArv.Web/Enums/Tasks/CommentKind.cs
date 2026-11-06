namespace DevArv.Web.Enums.Tasks;

public enum CommentKind
{
    COMMENT = 0,
    /// <summary>Cambios pedidos por una persona; se envían al agente en la siguiente ejecución.</summary>
    FEEDBACK = 1,
    /// <summary>Hallazgos de un agente revisor que rechazó la tarea.</summary>
    REVIEWER_NOTE = 2,
}
