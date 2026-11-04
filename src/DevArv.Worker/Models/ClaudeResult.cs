namespace DevArv.Worker.Models;

public sealed record ClaudeResult(
    bool IsError,
    string? Subtype,
    string? ResultText,
    decimal CostUsd,
    long InputTokens,
    long OutputTokens,
    long CacheReadTokens,
    long CacheWriteTokens,
    int? NumTurns,
    string? SessionId);
