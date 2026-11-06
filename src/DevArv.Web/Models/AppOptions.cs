namespace DevArv.Web.Models;

/// <summary>Ajustes generales de la aplicación (sección <c>App</c> del appsettings).</summary>
public sealed class AppOptions
{
    /// <summary>Zona horaria (id de IANA o de Windows) en la que se muestran las fechas. Vacía = la del servidor.</summary>
    public string TimeZone { get; set; } = "";

    /// <summary>URL pública del servidor; se usa en el enlace «Ver tarea en DevArv» del pull request.</summary>
    public string PublicUrl { get; set; } = "";
}
