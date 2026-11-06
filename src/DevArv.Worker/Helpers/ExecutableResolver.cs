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

        var path = FindOnPath(Executable);
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

    private static string? FindOnPath(string Executable)
    {
        if (Path.IsPathRooted(Executable)) return File.Exists(Executable) ? Executable : null;
        var exts = Path.HasExtension(Executable)
            ? new[] { "" }
            : (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';', StringSplitOptions.RemoveEmptyEntries);
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var dir in dirs)
        {
            foreach (var ext in exts)
            {
                var candidate = Path.Combine(dir.Trim('"'), Executable + ext);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }
}
