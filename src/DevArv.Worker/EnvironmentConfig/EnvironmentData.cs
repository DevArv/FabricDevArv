namespace DevArv.Worker.EnvironmentConfig;

/// <summary>Datos de conexión al servidor DevArv (sección EnvironmentData del appsettings).</summary>
public class EnvironmentData
{
    /// <summary>URL del servidor DevArv, p. ej. https://devarv.miempresa.com</summary>
    public string Url { get; set; } = "";

    /// <summary>Clave de esta PC, generada en «Mis PCs».</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Dirección base para el HttpClient (siempre termina en «/»).</summary>
    public Uri GetBaseAddress() => new(Url.TrimEnd('/') + "/");
}
