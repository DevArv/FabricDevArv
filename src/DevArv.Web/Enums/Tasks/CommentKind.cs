namespace DevArv.Web.Enums.Tasks;

public enum CommentKind
{
    Comment = 0,
    /// <summary>Cambios pedidos por una persona; se envían al agente en la siguiente ejecución.</summary>
    Feedback = 1,
    /// <summary>Hallazgos de un agente revisor que rechazó la tarea.</summary>
    ReviewerNote = 2,
}
