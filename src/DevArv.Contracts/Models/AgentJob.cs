using System.Text.Json.Serialization;

namespace DevArv.Contracts.Models;

/// <summary>Trabajo que el servidor entrega a un worker: una etapa de agente de una tarea.</summary>
/// <param name="MergeBaseBranch">
/// Arreglo de conflictos del pull request: antes de iniciar al agente, el worker integra <c>origin/BaseBranch</c> en la
/// rama de trabajo (<c>git merge</c>) y deja los conflictos sin resolver para que el agente los resuelva. Al terminar
/// rechaza el push si quedan marcadores de conflicto.
/// </param>
public sealed record AgentJob(
    [property: JsonPropertyName("runId")] long RunID,
    [property: JsonPropertyName("taskId")] int TaskID,
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
    IReadOnlyList<AttachmentInfo> Attachments,
    bool MergeBaseBranch = false);
