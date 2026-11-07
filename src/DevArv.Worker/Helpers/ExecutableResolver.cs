using DevArv.Worker.EnvironmentConfig;

namespace DevArv.Worker.Helpers;

/// <summary>
/// Resuelve el ejecutable de Claude. En Windows, la instalación por npm deja un "claude.cmd";
/// en ese caso se ejecuta node + cli.js directamente para no pasar argumentos por cmd.exe.
/// </summary>
public static class ExecutableResolver
{
    public sealed record Command(string FileName, IReadOnlyList<string> PrefixArgs);

    public static Command Resolve(string Executable)
    {
        if (!ProcessRunner.IsWindows) return new Command(Executable, []);

        var path = FindOnPath(
            Executable: Executable,
            Directories: EnvironmentTools.GetExecutableSearchDirectories(),
            Extensions: EnvironmentTools.GetExecutableExtensions());
        if (path is null) return new Command(Executable, []);

        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".cmd" or ".bat")
        {
            var dir = Path.GetDirectoryName(path)!;
            var cli = Path.Combine(dir, "node_modules", "@anthropic-ai", "claude-code", "cli.js");
            if (File.Exists(cli))
            {
                var node = File.Exists(Path.Combine(dir, "node.exe")) ? Path.Combine(dir, "node.exe") : "node";
                return new Command(node, [cli]);
            }
            return new Command("cmd.exe", ["/d", "/c", path]);
        }
        return new Command(path, []);
    }

    /// <summary>
    /// Ruta del ejecutable: la misma si ya es absoluta; si no, la primera coincidencia en <paramref name="Directories"/>
    /// probando <paramref name="Extensions"/> (sólo si el nombre no trae extensión). Null si no existe.
    /// </summary>
    internal static string? FindOnPath(string Executable, IReadOnlyList<string> Directories, IReadOnlyList<string> Extensions)
    {
        if (Path.IsPathRooted(Executable)) return File.Exists(Executable) ? Executable : null;
        var extensions = Path.HasExtension(Executable) ? [""] : Extensions;
        foreach (var directory in Directories)
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(directory, Executable + extension);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }
}
