namespace DevArv.Worker.Models;

public sealed record ProcessSpec(string FileName, IReadOnlyList<string> Arguments, string WorkingDirectory)
{
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();
    /// <summary>Variables heredadas del proceso del worker que no deben llegar al proceso hijo.</summary>
    public IReadOnlyCollection<string> RemovedEnvironment { get; init; } = [];
    public string? StandardInput { get; init; }
    /// <summary>Si se define, se usa como línea de argumentos ya escapada (caso cmd.exe) en lugar de Arguments.</summary>
    public string? RawArguments { get; init; }
}
