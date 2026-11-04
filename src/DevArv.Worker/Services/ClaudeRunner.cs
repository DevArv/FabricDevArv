using DevArv.Contracts.Models;
using Microsoft.Extensions.Options;
using DevArv.Worker.Models;
using DevArv.Worker.Helpers;

namespace DevArv.Worker.Services;

/// <summary>Ejecuta Claude Code en modo no interactivo dentro del worktree de la tarea.</summary>
public sealed class ClaudeRunner(IOptions<WorkerOptions> options, ILogger<ClaudeRunner> logger)
{
    private readonly WorkerOptions _options = options.Value;

    public async Task<ClaudeOutcome> RunAsync(AgentJob job, PreparedWorkspace ws, Action<string> log, CancellationToken ct)
    {
        var command = ExecutableResolver.Resolve(_options.Claude.Executable);
        var args = new List<string>(command.PrefixArgs);
        args.AddRange(BuildArguments(job, ws));

        var env = new Dictionary<string, string>(_options.Claude.Environment)
        {
            ["DISABLE_AUTOUPDATER"] = "1",
            ["DEVARV_TASK_ID"] = job.TaskId.ToString(),
            ["DEVARV_RUN_ID"] = job.RunId.ToString(),
        };
        if (!string.IsNullOrWhiteSpace(_options.Claude.AnthropicApiKey))
            env["ANTHROPIC_API_KEY"] = _options.Claude.AnthropicApiKey.Trim();

        var prompt = job.Prompt.Replace(PromptTokens.AttachmentsDir, ws.AttachmentsPath);
        var parser = new ClaudeStreamParser();
        var stderrTail = new Queue<string>();

        log($"$ claude {string.Join(' ', BuildArguments(job, ws, forDisplay: true))}");
        logger.LogInformation("Ejecutando Claude para la tarea #{TaskId} ({Stage})", job.TaskId, job.StageName);

        var spec = new ProcessSpec(command.FileName, args, ws.WorktreePath)
        {
            Environment = env,
            StandardInput = prompt,
        };

        var exit = await ProcessRunner.RunStreamingAsync(spec,
            onStdout: line =>
            {
                foreach (var text in parser.Parse(line)) log(text);
            },
            onStderr: line =>
            {
                if (string.IsNullOrWhiteSpace(line)) return;
                lock (stderrTail)
                {
                    stderrTail.Enqueue(line);
                    while (stderrTail.Count > 20) stderrTail.Dequeue();
                }
                log("stderr: " + line);
            },
            ct);

        string tail;
        lock (stderrTail) tail = string.Join("\n", stderrTail);
        return new ClaudeOutcome(exit, parser.Result, parser.Model, tail);
    }

    internal static List<string> BuildArguments(AgentJob job, PreparedWorkspace ws, bool forDisplay = false)
    {
        var args = new List<string> { "-p", "--output-format", "stream-json", "--verbose" };

        if (!string.IsNullOrWhiteSpace(job.Model)) args.AddRange(["--model", job.Model]);
        if (!string.IsNullOrWhiteSpace(job.PermissionMode)) args.AddRange(["--permission-mode", job.PermissionMode]);
        if (job.MaxTurns is { } turns) args.AddRange(["--max-turns", turns.ToString()]);
        if (!string.IsNullOrWhiteSpace(job.AppendSystemPrompt))
            args.AddRange(["--append-system-prompt", forDisplay ? "\"…\"" : job.AppendSystemPrompt]);
        if (job.Attachments.Count > 0) args.AddRange(["--add-dir", ws.AttachmentsPath]);

        // Opciones variádicas al final, cada una seguida de sus valores, para que no "absorban" otros argumentos.
        if (job.AllowedTools.Count > 0)
        {
            args.Add("--allowedTools");
            args.AddRange(job.AllowedTools.Select(t => forDisplay ? $"\"{t}\"" : t));
        }
        if (job.DisallowedTools.Count > 0)
        {
            args.Add("--disallowedTools");
            args.AddRange(job.DisallowedTools.Select(t => forDisplay ? $"\"{t}\"" : t));
        }
        args.AddRange(job.ExtraArgs);
        return args;
    }
}
