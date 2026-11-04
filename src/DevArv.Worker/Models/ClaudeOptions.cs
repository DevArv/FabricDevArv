namespace DevArv.Worker.Models;

public sealed class ClaudeOptions
{
    /// <summary>Ejecutable de Claude Code. "claude" lo busca en el PATH.</summary>
    public string Executable { get; set; } = "claude";

    /// <summary>
    /// Obligatoria. Única credencial de Claude: se pasa como ANTHROPIC_API_KEY y se ignoran la sesión de `claude login`
    /// y cualquier credencial de Anthropic heredada del entorno de la PC.
    /// </summary>
    public string? AnthropicApiKey { get; set; }

    /// <summary>Variables de entorno adicionales para el proceso de Claude (no se aceptan credenciales aquí).</summary>
    public Dictionary<string, string> Environment { get; set; } = new();
}
