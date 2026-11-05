using DevArv.Web.Enums.Workflow;

namespace DevArv.Web.DataModels;

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
    /// <summary>
    /// Skill de Claude Code con el que el agente aplica la etapa (p. ej. <c>revision</c>, en
    /// <c>.claude/skills/revision/SKILL.md</c> del repositorio). Nulo si no usa skill.
    /// </summary>
    public string? Skill { get; set; }
    /// <summary>Si es true el worker hace commit y push de los cambios al terminar.</summary>
    public bool PushChanges { get; set; }
    /// <summary>Si es true el agente debe terminar con "VEREDICTO: APROBADO/RECHAZADO".</summary>
    public bool RequiresDecision { get; set; }
    public int TimeoutMinutes { get; set; } = 60;

    // ---- Configuración para etapas de sistema ----
    public SystemAction SystemAction { get; set; }

    public bool RunsAutomatically => Kind is StageKind.Agent or StageKind.System;
}
