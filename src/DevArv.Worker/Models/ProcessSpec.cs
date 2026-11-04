namespace DevArv.Worker.Models;

public sealed record ProcessSpec(string FileName, IReadOnlyList<string> Arguments, string WorkingDirectory)
{
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();
    public string? StandardInput { get; init; }
    /// <summary>Si se define, se usa como línea de argumentos ya escapada (caso cmd.exe) en lugar de Arguments.</summary>
    public string? RawArguments { get; init; }
}
