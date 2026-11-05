using DevArv.Worker.Models;

namespace DevArv.Worker.Helpers;

/// <summary>
/// Credenciales que el worker exige en su appsettings (Claude: suscripción o API key; GitHub: token) (la conexión al servidor la valida EnvironmentTools).
/// No hay respaldo en la sesión de `claude`, en el Credential Manager de git ni en variables de la PC.
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
        if (IsEmpty(options.Claude.AnthropicApiKey) && IsEmpty(options.Claude.OAuthToken))
            missing.Add("Worker:Claude:OAuthToken (suscripción) o Worker:Claude:AnthropicApiKey");
        if (IsEmpty(options.Git.GitHubToken)) missing.Add("Worker:Git:GitHubToken");
        return missing;
    }

    /// <summary>Lanza <see cref="ArgumentException"/> si falta alguna credencial o si hay dos credenciales de Claude.</summary>
    public static void EnsureComplete(WorkerOptions options)
    {
        var missing = Missing(options);
        if (missing.Count > 0)
            throw new ArgumentException($"Faltan en el appsettings del worker: {string.Join(", ", missing)}. " +
                "Ejecuta scripts/Configurar-Claves.ps1 o completa appsettings.Local.json.");

        // Con las dos, Claude Code usaría la API key y se cobraría por uso aunque se quisiera usar la suscripción.
        if (!IsEmpty(options.Claude.AnthropicApiKey) && !IsEmpty(options.Claude.OAuthToken))
            throw new ArgumentException("Define sólo una credencial de Claude en el appsettings del worker: " +
                "Worker:Claude:OAuthToken (suscripción) o Worker:Claude:AnthropicApiKey (API key), no ambas.");
    }

    private static bool IsEmpty(string? value) => string.IsNullOrWhiteSpace(value);
}
