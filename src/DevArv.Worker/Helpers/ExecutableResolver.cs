namespace DevArv.Worker.Helpers;

/// <summary>
/// Resuelve el ejecutable de Claude. En Windows, la instalación por npm deja un "claude.cmd";
/// en ese caso se ejecuta node + cli.js directamente para no pasar argumentos por cmd.exe.
/// </summary>
public static class ExecutableResolver
{
    public sealed record Command(string FileName, IReadOnlyList<string> PrefixArgs);

    public static Command Resolve(string executable)
    {
        if (!ProcessRunner.IsWindows) return new Command(executable, []);

        var path = FindOnPath(executable);
        if (path is null) return new Command(executable, []);

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

    private static string? FindOnPath(string executable)
    {
        if (Path.IsPathRooted(executable)) return File.Exists(executable) ? executable : null;
        var exts = Path.HasExtension(executable)
            ? new[] { "" }
            : (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';', StringSplitOptions.RemoveEmptyEntries);
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var dir in dirs)
        {
            foreach (var ext in exts)
            {
                var candidate = Path.Combine(dir.Trim('"'), executable + ext);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }
}
