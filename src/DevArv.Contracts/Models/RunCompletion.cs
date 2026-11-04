namespace DevArv.Contracts.Models;

/// <summary>Resultado que el worker reporta al terminar (o fallar) una ejecución.</summary>
public sealed record RunCompletion(
    bool Success,
    bool Cancelled,
    string? ErrorMessage,
    string? ResultText,
    decimal CostUsd,
    long InputTokens,
    long OutputTokens,
    long CacheReadTokens,
    long CacheWriteTokens,
    int? NumTurns,
    string? SessionId,
    string? Model,
    string? HeadCommit,
    bool Pushed,
    IReadOnlyList<ChangedFile> ChangedFiles);
