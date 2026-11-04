namespace DevArv.Worker.Models;

public sealed class ServerOptions
{
    /// <summary>URL del servidor DevArv, p. ej. https://devarv.miempresa.com</summary>
    public string Url { get; set; } = "";

    /// <summary>Clave generada en «Mis PCs».</summary>
    public string ApiKey { get; set; } = "";
}
