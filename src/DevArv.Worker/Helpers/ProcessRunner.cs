using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using DevArv.Worker.Models;

namespace DevArv.Worker.Helpers;

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
