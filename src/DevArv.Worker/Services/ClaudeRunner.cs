using DevArv.Contracts.Models;
using Microsoft.Extensions.Options;
using DevArv.Worker.Models;
using DevArv.Worker.Helpers;

namespace DevArv.Worker.Services;

/// <summary>Ejecuta Claude Code en modo no interactivo dentro del worktree de la tarea.</summary>
public sealed class ClaudeRunner(IOptions<WorkerOptions> Options, ILogger<ClaudeRunner> Logger)
{
    private readonly WorkerOptions _Options = Options.Value;

    public async Task<ClaudeOutcome> RunAsync(AgentJob Job, PreparedWorkspace Workspace, Action<string> Log, CancellationToken CT)
    {
        var command = ExecutableResolver.Resolve(_Options.Claude.Executable);
        var args = new List<string>(command.PrefixArgs);
        args.AddRange(BuildArguments(Job, Workspace));

        var env = BuildEnvironment(_Options.Claude, Job);

        var prompt = Job.Prompt.Replace(PromptTokens.AttachmentsDir, Workspace.AttachmentsPath);
        var parser = new ClaudeStreamParser();
        var stderrTail = new Queue<string>();

        Log($"$ claude {string.Join(' ', BuildArguments(Job, Workspace, ForDisplay: true))}");
        Logger.LogInformation("Ejecutando Claude para la tarea #{TaskId} ({Stage})", Job.TaskId, Job.StageName);

        var spec = new ProcessSpec(command.FileName, args, Workspace.WorktreePath)
        {
            Environment = env,
            RemovedEnvironment = RequiredSettings.ClaudeCredentialVariables,
            StandardInput = prompt,
        };

        var exit = await ProcessRunner.RunStreamingAsync(spec,
            OnStdout: line =>
            {
                foreach (var text in parser.Parse(line)) Log(text);
            },
            OnStderr: line =>
            {
                if (string.IsNullOrWhiteSpace(line)) return;
                lock (stderrTail)
                {
                    stderrTail.Enqueue(line);
                    while (stderrTail.Count > 20) stderrTail.Dequeue();
                }
                Log("stderr: " + line);
            },
            CT);

        string tail;
        lock (stderrTail) tail = string.Join("\n", stderrTail);
        return new ClaudeOutcome(exit, parser.Result, parser.Model, tail);
    }

    /// <summary>
    /// Entorno del proceso de Claude. La única credencial es la del appsettings (Worker:Claude:OAuthToken o
    /// Worker:Claude:AnthropicApiKey); las que aparezcan en Worker:Claude:Environment se descartan.
    /// </summary>
    internal static Dictionary<string, string> BuildEnvironment(ClaudeOptions Options, AgentJob Job)
    {
        var env = Options.Environment
            .Where(e => !RequiredSettings.ClaudeCredentialVariables.Contains(e.Key, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(e => e.Key, e => e.Value);
        env["DISABLE_AUTOUPDATER"] = "1";
        env["DEVARV_TASK_ID"] = Job.TaskId.ToString();
        env["DEVARV_RUN_ID"] = Job.RunId.ToString();
        if (!string.IsNullOrWhiteSpace(Options.AnthropicApiKey))
            env["ANTHROPIC_API_KEY"] = Options.AnthropicApiKey.Trim();
        if (!string.IsNullOrWhiteSpace(Options.OAuthToken))
            env["CLAUDE_CODE_OAUTH_TOKEN"] = Options.OAuthToken.Trim();
        return env;
    }

    internal static List<string> BuildArguments(AgentJob Job, PreparedWorkspace Workspace, bool ForDisplay = false)
    {
        var args = new List<string> { "-p", "--output-format", "stream-json", "--verbose" };

        if (!string.IsNullOrWhiteSpace(Job.Model)) args.AddRange(["--model", Job.Model]);
        if (!string.IsNullOrWhiteSpace(Job.PermissionMode)) args.AddRange(["--permission-mode", Job.PermissionMode]);
        if (Job.MaxTurns is { } turns) args.AddRange(["--max-turns", turns.ToString()]);
        if (!string.IsNullOrWhiteSpace(Job.AppendSystemPrompt))
            args.AddRange(["--append-system-prompt", ForDisplay ? "\"…\"" : Job.AppendSystemPrompt]);
        if (Job.Attachments.Count > 0) args.AddRange(["--add-dir", Workspace.AttachmentsPath]);

        // Opciones variádicas al final, cada una seguida de sus valores, para que no "absorban" otros argumentos.
        if (Job.AllowedTools.Count > 0)
        {
            args.Add("--allowedTools");
            args.AddRange(Job.AllowedTools.Select(t => ForDisplay ? $"\"{t}\"" : t));
        }
        if (Job.DisallowedTools.Count > 0)
        {
            args.Add("--disallowedTools");
            args.AddRange(Job.DisallowedTools.Select(t => ForDisplay ? $"\"{t}\"" : t));
        }
        args.AddRange(Job.ExtraArgs);
        return args;
    }
}
