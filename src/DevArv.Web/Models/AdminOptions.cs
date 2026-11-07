namespace DevArv.Web.Models;

/// <summary>
/// Administrador inicial (sección <c>Admin</c> del appsettings). Se crea al arrancar sólo si todavía no existe
/// ningún usuario. La contraseña va en <c>appsettings.Local.json</c>, ignorado por git.
/// </summary>
public sealed class AdminOptions
{
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
}
