using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using DevArv.Worker.Models;

namespace DevArv.Worker.Helpers;

public static class ProcessRunner
{
    private static readonly Encoding _Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Ejecuta un proceso y entrega cada línea de stdout/stderr a medida que llega.</summary>
    public static async Task<int> RunStreamingAsync(
        ProcessSpec Spec, Action<string> OnStdout, Action<string> OnStderr, CancellationToken CT)
    {
        var psi = new ProcessStartInfo
        {
            FileName = Spec.FileName,
            WorkingDirectory = Spec.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = _Utf8,
            StandardErrorEncoding = _Utf8,
            StandardInputEncoding = _Utf8,
        };
        if (Spec.RawArguments is not null) psi.Arguments = Spec.RawArguments;
        else foreach (var arg in Spec.Arguments) psi.ArgumentList.Add(arg);
        foreach (var key in Spec.RemovedEnvironment) psi.Environment.Remove(key);
        foreach (var (key, value) in Spec.Environment) psi.Environment[key] = value;

        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) OnStdout(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) OnStderr(e.Data); };

        if (!process.Start()) throw new InvalidOperationException($"No se pudo iniciar {Spec.FileName}.");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            if (Spec.StandardInput is not null)
                await process.StandardInput.WriteAsync(Spec.StandardInput.AsMemory(), CT);
            process.StandardInput.Close();
            await process.WaitForExitAsync(CT);
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
    public static async Task<ProcessResult> RunAsync(ProcessSpec Spec, CancellationToken CT)
    {
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var code = await RunStreamingAsync(Spec,
            line => { lock (stdout) stdout.AppendLine(line); },
            line => { lock (stderr) stderr.AppendLine(line); }, CT);
        return new ProcessResult(code, stdout.ToString(), stderr.ToString());
    }

    private static void Kill(Process Process)
    {
        try
        {
            if (!Process.HasExited) Process.Kill(entireProcessTree: true);
            Process.WaitForExit(10_000);
        }
        catch (Exception)
        {
            // Ya terminó o no se puede terminar; no hay nada más que hacer.
        }
    }

    public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
}
