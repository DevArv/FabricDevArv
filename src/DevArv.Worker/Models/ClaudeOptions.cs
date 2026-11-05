namespace DevArv.Worker.Models;

public sealed class ClaudeOptions
{
    /// <summary>Ejecutable de Claude Code. "claude" lo busca en el PATH.</summary>
    public string Executable { get; set; } = "claude";

    /// <summary>
    /// API key de la consola de Anthropic (sk-ant-api…, se paga por uso). Se pasa como ANTHROPIC_API_KEY.
    /// Define ésta o <see cref="OAuthToken"/>, no ambas.
    /// </summary>
    public string? AnthropicApiKey { get; set; }

    /// <summary>
    /// Token de la suscripción Pro/Max (sk-ant-oat…, se genera una vez con `claude setup-token`). Se pasa como
    /// CLAUDE_CODE_OAUTH_TOKEN y el consumo cuenta contra los límites del plan. Define éste o <see cref="AnthropicApiKey"/>.
    /// </summary>
    public string? OAuthToken { get; set; }

    /// <summary>Variables de entorno adicionales para el proceso de Claude (las credenciales de Anthropic se descartan).</summary>
    public Dictionary<string, string> Environment { get; set; } = new();
}
