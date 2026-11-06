using DevArv.Web.Enums.Runs;
using DevArv.Web.Enums.Workflow;

namespace DevArv.Web.DataModels;

/// <summary>Una ejecución de una etapa automática (agente o sistema) sobre una tarea.</summary>
public class TaskRun
{
    public long ID { get; set; }
    public int TaskID { get; set; }
    public TaskItem Task { get; set; } = null!;
    public int StageID { get; set; }
    public string StageName { get; set; } = "";
    public StageKind RunnerKind { get; set; }
    public RunStatus Status { get; set; }
    public RunDecision Decision { get; set; }

    public int? WorkerID { get; set; }
    public WorkerNode? Worker { get; set; }
    /// <summary>Copia de TaskItem.PreferredWorkerId al encolar.</summary>
    public int? PinnedWorkerID { get; set; }
    /// <summary>
    /// Ejecución pedida para arreglar los conflictos del pull request: el worker integra la rama base en la rama de
    /// trabajo antes de iniciar al agente y el prompt le pide resolver los conflictos.
    /// </summary>
    public bool ResolveConflicts { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public DateTimeOffset? LastActivityAt { get; set; }

    public string? Model { get; set; }
    public decimal CostUsd { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long CacheReadTokens { get; set; }
    public long CacheWriteTokens { get; set; }
    public int? NumTurns { get; set; }
    public string? SessionID { get; set; }

    public string? PromptSnapshot { get; set; }
    public string? Summary { get; set; }
    public string Log { get; set; } = "";
    public string? ErrorMessage { get; set; }
    public string? ChangedFilesJson { get; set; }
    public string? HeadCommit { get; set; }
    public bool CancelRequested { get; set; }

    public long TotalTokens => InputTokens + OutputTokens + CacheReadTokens + CacheWriteTokens;
    public TimeSpan? Duration => StartedAt is null ? null : (FinishedAt ?? DateTimeOffset.UtcNow) - StartedAt.Value;
}
