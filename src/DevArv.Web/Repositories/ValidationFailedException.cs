namespace DevArv.Web.Repositories;

/// <summary>
/// Una o varias reglas de negocio no se cumplen. Cada error indica el campo del formulario
/// (p. ej. "Input.Name") para que el PageModel lo muestre en ModelState.
/// </summary>
public sealed class ValidationFailedException : Exception
{
    public IReadOnlyList<(string Field, string Message)> Errors { get; }

    public ValidationFailedException(string field, string message) : base(message)
    {
        Errors = [(field, message)];
    }

    public ValidationFailedException(IReadOnlyList<(string Field, string Message)> errors)
        : base(string.Join(" ", errors.Select(e => e.Message)))
    {
        Errors = errors;
    }
}
