using DevArv.Worker.Models;

namespace DevArv.Worker.Helpers;

/// <summary>
/// Indica, sin exponer valores, de dónde saldrán las credenciales de Claude y de GitHub.
/// Sigue la misma prioridad que <c>ClaudeRunner</c>: AnthropicApiKey sobrescribe a Claude:Environment,
/// que a su vez sobrescribe a las variables heredadas del proceso; Claude Code prefiere la API key sobre el token de suscripción.
/// </summary>
public static class CredentialSources
{
    public const string ApiKeyVariable = "ANTHROPIC_API_KEY";
    public const string OAuthTokenVariable = "CLAUDE_CODE_OAUTH_TOKEN";

    public static CredentialSource ForClaude(ClaudeOptions options, Func<string, string?> getEnvironmentVariable)
    {
        if (HasValue(options.AnthropicApiKey))
            return new CredentialSource(true, "API key de Worker:Claude:AnthropicApiKey");
        if (HasValue(options.Environment.GetValueOrDefault(ApiKeyVariable)))
            return new CredentialSource(true, $"{ApiKeyVariable} de Worker:Claude:Environment");
        if (HasValue(getEnvironmentVariable(ApiKeyVariable)))
            return new CredentialSource(true, $"variable de entorno {ApiKeyVariable}");
        if (HasValue(options.Environment.GetValueOrDefault(OAuthTokenVariable)))
            return new CredentialSource(true, $"token de suscripción {OAuthTokenVariable} de Worker:Claude:Environment");
        if (HasValue(getEnvironmentVariable(OAuthTokenVariable)))
            return new CredentialSource(true, $"variable de entorno {OAuthTokenVariable}");
        return new CredentialSource(false, "sesión de `claude` iniciada por el usuario del worker");
    }

    public static CredentialSource ForGitHub(GitOptions options) =>
        HasValue(options.GitHubToken)
            ? new CredentialSource(true, "token de Worker:Git:GitHubToken")
            : new CredentialSource(false, "credenciales de git de esta PC (Git Credential Manager, SSH)");

    private static bool HasValue(string? value) => !string.IsNullOrWhiteSpace(value);
}
