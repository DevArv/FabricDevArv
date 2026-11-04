namespace DevArv.Worker.Models;

public sealed record ClaudeOutcome(int ExitCode, ClaudeResult? Result, string? Model, string StdErrTail)
{
    public bool Success => ExitCode == 0 && Result is { IsError: false };
}
