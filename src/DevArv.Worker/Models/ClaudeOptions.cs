namespace DevArv.Worker.Models;

public sealed class ClaudeOptions
{
    /// <summary>Ejecutable de Claude Code. "claude" lo busca en el PATH.</summary>
    public string Executable { get; set; } = "claude";

    /// <summary>Opcional. Si se define se pasa como ANTHROPIC_API_KEY; si no, se usa la sesión iniciada con `claude login`.</summary>
    public string? AnthropicApiKey { get; set; }

    /// <summary>Variables de entorno adicionales para el proceso de Claude.</summary>
    public Dictionary<string, string> Environment { get; set; } = new();
}
