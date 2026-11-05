namespace DevArv.Web.Models;

public sealed class GitHubOptions
{
    /// <summary>
    /// Token (fine-grained o classic) con permiso de lectura de contenido y escritura de pull requests.
    /// Se lee del appsettings del servidor (appsettings.Local.json, ignorado por git).
    /// </summary>
    public string Token { get; set; } = "";
    public string ApiBaseUrl { get; set; } = "https://api.github.com/";
    public string WebBaseUrl { get; set; } = "https://github.com";
}
