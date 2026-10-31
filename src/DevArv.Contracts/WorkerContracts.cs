namespace DevArv.Contracts;

/// <summary>Cabecera HTTP con la que cada worker (PC) se autentica ante el servidor.</summary>
public static class WorkerHeaders
{
    public const string ApiKey = "X-DevArv-Key";
}

public sealed record HeartbeatRequest(string MachineName, string Version, string? OsDescription, int RunningJobs);

public sealed record HeartbeatResponse(DateTimeOffset ServerTime, string WorkerName);

public sealed record ClaimRequest(string MachineName);

/// <summary>Trabajo que el servidor entrega a un worker: una etapa de agente de una tarea.</summary>
public sealed record AgentJob(
    long RunId,
    int TaskId,
    string TaskTitle,
    string StageName,
    RepoInfo Repo,
    string BaseBranch,
    string WorkBranch,
    string Prompt,
    string? AppendSystemPrompt,
    string? Model,
    string PermissionMode,
    IReadOnlyList<string> AllowedTools,
    IReadOnlyList<string> DisallowedTools,
    int? MaxTurns,
    IReadOnlyList<string> ExtraArgs,
    bool PushChanges,
    int TimeoutMinutes,
    string CommitMessage,
    IReadOnlyList<AttachmentInfo> Attachments);

public sealed record RepoInfo(string Owner, string Name, string CloneUrl);

public sealed record AttachmentInfo(long Id, string FileName, string ContentType, long Size)
{
    /// <summary>Nombre con el que el worker guarda el adjunto en disco (el prompt lo referencia igual).</summary>
    public string LocalFileName => $"{Id}-{FileName}";
}

/// <summary>Marcadores que el worker reemplaza en el prompt antes de ejecutar Claude.</summary>
public static class PromptTokens
{
    /// <summary>Ruta absoluta del directorio local con los adjuntos de la tarea.</summary>
    public const string AttachmentsDir = "{{attachmentsDir}}";
}

public sealed record LogChunk(string Text);

public sealed record LogChunkResponse(bool CancelRequested);

public sealed record ChangedFile(string Status, string Path);

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
