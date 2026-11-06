using DevArv.Web.Constants;
using DevArv.Web.DataModels;
using DevArv.Web.DatabaseConnection;
using DevArv.Web.Enums.Workflow;
using DevArv.Web.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace DevArv.Web.Repositories;

public class StagesRepository(AppDbContext Db)
{
    public Task<List<Stage>> GetAllAsync(CancellationToken CT) =>
        Db.Stages.AsNoTracking().OrderBy(s => s.Order).ToListAsync(CT);

    /// <summary>Todas las columnas excepto <paramref name="ExcludedID"/> (destinos posibles al editar una columna).</summary>
    public Task<List<Stage>> GetOthersAsync(int? ExcludedID, CancellationToken CT) =>
        Db.Stages.AsNoTracking().Where(s => s.Id != ExcludedID).OrderBy(s => s.Order).ToListAsync(CT);

    public Task<Dictionary<int, int>> GetTaskCountsAsync(CancellationToken CT) =>
        Db.Tasks.GroupBy(t => t.StageId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, CT);

    public StageEditViewModel NewEdit() => new()
    {
        PromptTemplate = DefaultPrompts.DEVELOPER,
        AllowedTools = DefaultPrompts.DEVELOPER_ALLOWED_TOOLS,
    };

    public async Task<StageEditViewModel?> GetEditAsync(int ID, CancellationToken CT)
    {
        var stage = await Db.Stages.AsNoTracking().FirstOrDefaultAsync(x => x.Id == ID, CT);
        return stage is null
            ? null
            : new StageEditViewModel
            {
                Name = stage.Name, Icon = stage.Icon, Kind = stage.Kind, Description = stage.Description, IsInitial = stage.IsInitial,
                IsMergeDestination = stage.IsMergeDestination,
                SuccessStageId = stage.SuccessStageId, RejectStageId = stage.RejectStageId, Model = stage.Model,
                PermissionMode = stage.PermissionMode, AllowedTools = stage.AllowedTools, DisallowedTools = stage.DisallowedTools,
                MaxTurns = stage.MaxTurns, ExtraArgs = stage.ExtraArgs, PromptTemplate = stage.PromptTemplate,
                AppendSystemPrompt = stage.AppendSystemPrompt, Skill = stage.Skill, PushChanges = stage.PushChanges,
                RequiresDecision = stage.RequiresDecision, TimeoutMinutes = stage.TimeoutMinutes, SystemAction = stage.SystemAction,
            };
    }

    /// <summary>Reglas de negocio de una columna. Devuelve todos los errores (campo + mensaje); vacío si es válida.</summary>
    public async Task<List<(string Field, string Message)>> GetValidationErrorsAsync(
        int? ID, StageEditViewModel Record, CancellationToken CT)
    {
        var others = await GetOthersAsync(
            ExcludedID: ID,
            CT: CT);
        var errors = new List<(string Field, string Message)>();

        if (!PermissionModes.ALL.Contains(Record.PermissionMode))
            errors.Add(("Input.PermissionMode", "Modo de permisos inválido."));
        if (Record.SuccessStageId is { } ok && !others.Any(s => s.Id == ok))
            errors.Add(("Input.SuccessStageId", "Columna destino inválida."));
        if (Record.RejectStageId is { } ko && !others.Any(s => s.Id == ko))
            errors.Add(("Input.RejectStageId", "Columna de rechazo inválida."));
        if (Record.Kind == StageKind.System && Record.SystemAction == SystemAction.None)
            errors.Add(("Input.SystemAction", "Elige la acción de sistema."));
        if (Record.RequiresDecision && Record.RejectStageId is null)
            errors.Add(("Input.RejectStageId", "Una etapa con veredicto necesita columna de rechazo."));

        var badArg = Lines(Record.ExtraArgs).FirstOrDefault(a => a.Contains(' ') && a.StartsWith("--"));
        if (badArg is not null)
            errors.Add(("Input.ExtraArgs", $"Escribe un argumento por línea («{badArg}» debe ir en dos líneas)."));

        return errors;
    }

    /// <summary>
    /// Crea (ID nulo) o actualiza una columna. Devuelve false si la columna a editar no existe.
    /// Lanza ValidationFailedException con todos los errores si la columna no cumple las reglas.
    /// </summary>
    public async Task<bool> SaveAsync(int? ID, StageEditViewModel Record, CancellationToken CT)
    {
        var errors = await GetValidationErrorsAsync(
            ID: ID,
            Record: Record,
            CT: CT);
        if (errors.Count > 0) throw new ValidationFailedException(errors);

        Stage stage;
        if (ID is null)
        {
            stage = new Stage { Order = (await Db.Stages.MaxAsync(s => (int?)s.Order, CT) ?? 0) + 10 };
            Db.Stages.Add(stage);
        }
        else
        {
            var existing = await Db.Stages.FindAsync([ID.Value], CT);
            if (existing is null) return false;
            stage = existing;
        }

        stage.Name = Record.Name.Trim();
        stage.Icon = string.IsNullOrWhiteSpace(Record.Icon) ? "📋" : Record.Icon.Trim();
        stage.Kind = Record.Kind;
        stage.Description = Record.Description;
        stage.SuccessStageId = Record.SuccessStageId;
        stage.RejectStageId = Record.RejectStageId;
        stage.Model = string.IsNullOrWhiteSpace(Record.Model) ? null : Record.Model.Trim();
        stage.PermissionMode = Record.PermissionMode;
        stage.AllowedTools = Record.AllowedTools;
        stage.DisallowedTools = Record.DisallowedTools;
        stage.MaxTurns = Record.MaxTurns;
        stage.ExtraArgs = Record.ExtraArgs;
        stage.PromptTemplate = Record.PromptTemplate;
        stage.AppendSystemPrompt = Record.AppendSystemPrompt;
        stage.Skill = Record.Kind == StageKind.Agent ? NormalizeSkill(Record.Skill) : null;
        stage.PushChanges = Record.Kind == StageKind.Agent && Record.PushChanges;
        stage.RequiresDecision = Record.Kind == StageKind.Agent && Record.RequiresDecision;
        stage.TimeoutMinutes = Record.TimeoutMinutes;
        stage.SystemAction = Record.Kind == StageKind.System ? Record.SystemAction : SystemAction.None;

        if (Record.IsInitial)
        {
            await Db.Stages.Where(s => s.IsInitial).ForEachAsync(s => s.IsInitial = false, CT);
            stage.IsInitial = true;
        }
        else
        {
            stage.IsInitial = false;
        }

        // Sólo una columna recibe las tareas cuyo pull request se integró.
        if (Record.IsMergeDestination)
            await Db.Stages.Where(s => s.IsMergeDestination).ForEachAsync(s => s.IsMergeDestination = false, CT);
        stage.IsMergeDestination = Record.IsMergeDestination;

        await Db.SaveChangesAsync(CT);
        return true;
    }

    /// <summary>Sube (<paramref name="Direction"/> &lt; 0) o baja (&gt; 0) una columna en el orden del tablero.</summary>
    public async Task MoveAsync(int ID, int Direction, CancellationToken CT)
    {
        var stages = await Db.Stages.OrderBy(s => s.Order).ToListAsync(CT);
        var index = stages.FindIndex(s => s.Id == ID);
        var other = index + Math.Sign(Direction);
        if (index < 0 || other < 0 || other >= stages.Count) return;

        (stages[index], stages[other]) = (stages[other], stages[index]);
        for (var i = 0; i < stages.Count; i++) stages[i].Order = (i + 1) * 10;
        await Db.SaveChangesAsync(CT);
    }

    public async Task DeleteAsync(int ID, CancellationToken CT)
    {
        if (await Db.Tasks.AnyAsync(t => t.StageId == ID, CT))
            throw new ValidationFailedException(nameof(ID),
                "No se puede eliminar una columna que tiene tareas (incluidas las archivadas). Muévelas primero.");
        if (await Db.TaskRuns.AnyAsync(r => r.StageId == ID, CT))
            throw new ValidationFailedException(nameof(ID),
                "La columna tiene historial de ejecuciones; en lugar de eliminarla, déjala sin tareas o renómbrala.");

        var stage = await Db.Stages.FindAsync([ID], CT);
        if (stage is null) return;
        Db.Stages.Remove(stage);
        await Db.SaveChangesAsync(CT);
    }

    /// <summary>Nombre del skill sin espacios ni la barra de <c>/revision</c>; nulo si está vacío.</summary>
    private static string? NormalizeSkill(string? Skill)
    {
        var name = (Skill ?? "").Trim().TrimStart('/').Trim();
        return name.Length == 0 ? null : name;
    }

    private static IEnumerable<string> Lines(string? Text) =>
        (Text ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
