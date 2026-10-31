namespace DevArv.Web.Domain;

public class Project
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Color en hexadecimal para el chip del proyecto (#22c55e).</summary>
    public string Color { get; set; } = "#3b82f6";
    public string RepoOwner { get; set; } = "";
    public string RepoName { get; set; } = "";
    public string DefaultBranch { get; set; } = "main";
    /// <summary>Prefijo de las ramas de trabajo, p. ej. "devarv/" → devarv/12-mi-tarea.</summary>
    public string BranchPrefix { get; set; } = "devarv/";
    /// <summary>Instrucciones extra para todos los agentes de este proyecto (se agregan al system prompt).</summary>
    public string? AgentInstructions { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public string RepoFullName => $"{RepoOwner}/{RepoName}";
}

/// <summary>Columna del tablero Kanban y su configuración de agente o acción.</summary>
public class Stage
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Order { get; set; }
    public StageKind Kind { get; set; }
    public string Icon { get; set; } = "📋";
    public string? Description { get; set; }
    /// <summary>Columna donde nacen las tareas nuevas.</summary>
    public bool IsInitial { get; set; }

    /// <summary>Destino cuando la etapa termina bien / se aprueba (✓).</summary>
    public int? SuccessStageId { get; set; }
    /// <summary>Destino cuando se rechaza / se piden cambios (⊘).</summary>
    public int? RejectStageId { get; set; }

    // ---- Configuración para etapas de agente ----
    public string? Model { get; set; }
    /// <summary>default, acceptEdits, plan, dontAsk o bypassPermissions.</summary>
    public string PermissionMode { get; set; } = "acceptEdits";
    /// <summary>Herramientas permitidas, una por línea (p. ej. Bash(dotnet test:*)).</summary>
    public string? AllowedTools { get; set; }
    /// <summary>Herramientas prohibidas, una por línea.</summary>
    public string? DisallowedTools { get; set; }
    public int? MaxTurns { get; set; }
    /// <summary>Argumentos adicionales para el CLI de Claude, uno por línea.</summary>
    public string? ExtraArgs { get; set; }
    public string? PromptTemplate { get; set; }
    public string? AppendSystemPrompt { get; set; }
    /// <summary>Si es true el worker hace commit y push de los cambios al terminar.</summary>
    public bool PushChanges { get; set; }
    /// <summary>Si es true el agente debe terminar con "VEREDICTO: APROBADO/RECHAZADO".</summary>
    public bool RequiresDecision { get; set; }
    public int TimeoutMinutes { get; set; } = 60;

    // ---- Configuración para etapas de sistema ----
    public SystemAction SystemAction { get; set; }

    public bool RunsAutomatically => Kind is StageKind.Agent or StageKind.System;
}

public class TaskItem
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public int ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    public int StageId { get; set; }
    public Stage Stage { get; set; } = null!;
    public WorkStatus Status { get; set; }
    public Priority Priority { get; set; } = Priority.Medium;
    public Complexity Complexity { get; set; }
    public string? Labels { get; set; }

    public string BaseBranch { get; set; } = "main";
    public string? WorkBranch { get; set; }
    public int? PullRequestNumber { get; set; }
    public string? PullRequestUrl { get; set; }

    /// <summary>Si se define, sólo este worker (PC) puede ejecutar las etapas de agente.</summary>
    public int? PreferredWorkerId { get; set; }
    public WorkerNode? PreferredWorker { get; set; }
    public string? LastWorkerName { get; set; }
    /// <summary>Modelo que reemplaza el de la etapa para esta tarea (opus, sonnet, nombre completo…).</summary>
    public string? ModelOverride { get; set; }

    public string? CreatedById { get; set; }
    public string CreatedByName { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset StageEnteredAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ArchivedAt { get; set; }

    public decimal TotalCostUsd { get; set; }
    public long TotalTokens { get; set; }
    public string? LastError { get; set; }

    public List<TaskAttachment> Attachments { get; set; } = [];
    public List<TaskRun> Runs { get; set; } = [];
    public List<TaskComment> Comments { get; set; } = [];
    public List<TaskEvent> Events { get; set; } = [];
}

public class TaskAttachment
{
    public long Id { get; set; }
    public int TaskId { get; set; }
    public TaskItem Task { get; set; } = null!;
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long Size { get; set; }
    public AttachmentKind Kind { get; set; }
    /// <summary>Nombre del archivo en disco (GUID + extensión). Nunca se usa el nombre del usuario.</summary>
    public string StoredName { get; set; } = "";
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Una ejecución de una etapa automática (agente o sistema) sobre una tarea.</summary>
public class TaskRun
{
    public long Id { get; set; }
    public int TaskId { get; set; }
    public TaskItem Task { get; set; } = null!;
    public int StageId { get; set; }
    public string StageName { get; set; } = "";
    public StageKind RunnerKind { get; set; }
    public RunStatus Status { get; set; }
    public RunDecision Decision { get; set; }

    public int? WorkerId { get; set; }
    public WorkerNode? Worker { get; set; }
    /// <summary>Copia de TaskItem.PreferredWorkerId al encolar.</summary>
    public int? PinnedWorkerId { get; set; }

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
    public string? SessionId { get; set; }

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

public class TaskComment
{
    public long Id { get; set; }
    public int TaskId { get; set; }
    public TaskItem Task { get; set; } = null!;
    public CommentKind Kind { get; set; }
    public string Author { get; set; } = "";
    public string Body { get; set; } = "";
    public long? RunId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Historial: "agents-vm tomó el pedido «Desarrollador»", "Juan aprobó", etc.</summary>
public class TaskEvent
{
    public long Id { get; set; }
    public int TaskId { get; set; }
    public TaskItem Task { get; set; } = null!;
    public string Actor { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Una PC registrada que ejecuta el worker de agentes.</summary>
public class WorkerNode
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>SHA-256 (hex) de la API key. La key en claro sólo se muestra al crearla.</summary>
    public string KeyHash { get; set; } = "";
    public string KeyPrefix { get; set; } = "";
    public string? MachineName { get; set; }
    public string? Version { get; set; }
    public string? OsDescription { get; set; }
    public int RunningJobs { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSeenAt { get; set; }

    public bool IsOnline(DateTimeOffset now) => LastSeenAt is { } seen && now - seen < TimeSpan.FromMinutes(2);
}
