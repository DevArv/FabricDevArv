using DevArv.Worker.Models;

namespace DevArv.Worker.Helpers;

/// <summary>
/// Ajustes que el worker exige en su appsettings antes de tomar trabajos. Las credenciales sólo se leen de ahí:
/// no hay respaldo en la sesión de `claude`, en el Credential Manager de git ni en variables de la PC.
/// </summary>
public static class RequiredSettings
{
    /// <summary>Variables de credenciales de Anthropic que nunca se heredan del entorno de la PC.</summary>
    public static readonly IReadOnlyList<string> ClaudeCredentialVariables =
        ["ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN", "CLAUDE_CODE_OAUTH_TOKEN"];

    /// <summary>Devuelve las claves de configuración que faltan (vacío si todo está completo).</summary>
    public static IReadOnlyList<string> Missing(WorkerOptions options)
    {
        var missing = new List<string>();
        if (IsEmpty(options.Server.Url)) missing.Add("Worker:Server:Url");
        if (IsEmpty(options.Server.ApiKey)) missing.Add("Worker:Server:ApiKey");
        if (IsEmpty(options.Claude.AnthropicApiKey)) missing.Add("Worker:Claude:AnthropicApiKey");
        if (IsEmpty(options.Git.GitHubToken)) missing.Add("Worker:Git:GitHubToken");
        return missing;
    }

    private static bool IsEmpty(string? value) => string.IsNullOrWhiteSpace(value);
}
