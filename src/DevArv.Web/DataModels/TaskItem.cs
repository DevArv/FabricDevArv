using DevArv.Web.Enums.Tasks;

namespace DevArv.Web.DataModels;

public class TaskItem
{
    public int ID { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public int ProjectID { get; set; }
    public Project Project { get; set; } = null!;
    public int StageID { get; set; }
    public Stage Stage { get; set; } = null!;
    public WorkStatus Status { get; set; }
    public Priority Priority { get; set; } = Priority.MEDIUM;
    public Complexity Complexity { get; set; }
    public string? Labels { get; set; }

    public string BaseBranch { get; set; } = "main";
    public string? WorkBranch { get; set; }
    public int? PullRequestNumber { get; set; }
    public string? PullRequestUrl { get; set; }
    /// <summary>Cuándo DevArv detectó (o hizo) el merge del pull request actual; nulo si sigue sin integrarse.</summary>
    public DateTimeOffset? PullRequestMergedAt { get; set; }
    /// <summary>
    /// GitHub informó que el pull request actual tiene conflictos con la rama base (no se puede integrar).
    /// Lo actualiza el orquestador al consultar el pull request.
    /// </summary>
    public bool PullRequestHasConflicts { get; set; }

    /// <summary>Si se define, sólo este worker (PC) puede ejecutar las etapas de agente.</summary>
    public int? PreferredWorkerID { get; set; }
    public WorkerNode? PreferredWorker { get; set; }
    public string? LastWorkerName { get; set; }
    /// <summary>Modelo que reemplaza el de la etapa para esta tarea (opus, sonnet, nombre completo…).</summary>
    public string? ModelOverride { get; set; }

    public string? CreatedByID { get; set; }
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
