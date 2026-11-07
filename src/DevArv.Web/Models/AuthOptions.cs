namespace DevArv.Web.Models;

/// <summary>Ajustes de autenticación (sección <c>Auth</c> del appsettings).</summary>
public sealed class AuthOptions
{
    /// <summary>
    /// Permite el registro público de usuarios. Si es false sólo se puede registrar el primer usuario
    /// (mientras no exista ninguno).
    /// </summary>
    public bool AllowRegistration { get; set; }
}
