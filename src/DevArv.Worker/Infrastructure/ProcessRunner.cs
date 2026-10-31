using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace DevArv.Worker.Infrastructure;

public sealed record ProcessSpec(string FileName, IReadOnlyList<string> Arguments, string WorkingDirectory)
{
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();
    public string? StandardInput { get; init; }
    /// <summary>Si se define, se usa como línea de argumentos ya escapada (caso cmd.exe) en lugar de Arguments.</summary>
    public string? RawArguments { get; init; }
}

public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Ok => ExitCode == 0;
}

public static class ProcessRunner
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Ejecuta un proceso y entrega cada línea de stdout/stderr a medida que llega.</summary>
    public static async Task<int> RunStreamingAsync(
        ProcessSpec spec, Action<string> onStdout, Action<string> onStderr, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = spec.FileName,
            WorkingDirectory = spec.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8,
            StandardInputEncoding = Utf8,
        };
        if (spec.RawArguments is not null) psi.Arguments = spec.RawArguments;
        else foreach (var arg in spec.Arguments) psi.ArgumentList.Add(arg);
        foreach (var (key, value) in spec.Environment) psi.Environment[key] = value;

        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) onStdout(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) onStderr(e.Data); };

        if (!process.Start()) throw new InvalidOperationException($"No se pudo iniciar {spec.FileName}.");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            if (spec.StandardInput is not null)
                await process.StandardInput.WriteAsync(spec.StandardInput.AsMemory(), ct);
            process.StandardInput.Close();
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            throw;
        }
        catch (IOException) when (process.HasExited)
        {
            // El proceso terminó antes de leer todo el stdin; el código de salida explica el motivo.
            await process.WaitForExitAsync(CancellationToken.None);
        }
        return process.ExitCode;
    }

    /// <summary>Ejecuta un proceso corto y devuelve su salida completa.</summary>
    public static async Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken ct)
    {
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var code = await RunStreamingAsync(spec,
            line => { lock (stdout) stdout.AppendLine(line); },
            line => { lock (stderr) stderr.AppendLine(line); }, ct);
        return new ProcessResult(code, stdout.ToString(), stderr.ToString());
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            process.WaitForExit(10_000);
        }
        catch (Exception)
        {
            // Ya terminó o no se puede terminar; no hay nada más que hacer.
        }
    }

    public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
}

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
