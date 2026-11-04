using DevArv.Web.Constants;
using DevArv.Web.DataModels;
using DevArv.Web.DatabaseConnection;
using DevArv.Web.Enums.Workflow;
using DevArv.Web.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace DevArv.Web.Repositories;

public class StagesRepository(AppDbContext db)
{
    public Task<List<Stage>> GetAllAsync(CancellationToken ct) =>
        db.Stages.AsNoTracking().OrderBy(s => s.Order).ToListAsync(ct);

    /// <summary>Todas las columnas excepto <paramref name="excludedID"/> (destinos posibles al editar una columna).</summary>
    public Task<List<Stage>> GetOthersAsync(int? excludedID, CancellationToken ct) =>
        db.Stages.AsNoTracking().Where(s => s.Id != excludedID).OrderBy(s => s.Order).ToListAsync(ct);

    public Task<Dictionary<int, int>> GetTaskCountsAsync(CancellationToken ct) =>
        db.Tasks.GroupBy(t => t.StageId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

    public StageEditViewModel NewEdit() => new()
    {
        PromptTemplate = DefaultPrompts.Developer,
        AllowedTools = DefaultPrompts.DeveloperAllowedTools,
    };

    public async Task<StageEditViewModel?> GetEditAsync(int id, CancellationToken ct)
    {
        var s = await db.Stages.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return s is null
            ? null
            : new StageEditViewModel
            {
                Name = s.Name, Icon = s.Icon, Kind = s.Kind, Description = s.Description, IsInitial = s.IsInitial,
                SuccessStageId = s.SuccessStageId, RejectStageId = s.RejectStageId, Model = s.Model,
                PermissionMode = s.PermissionMode, AllowedTools = s.AllowedTools, DisallowedTools = s.DisallowedTools,
                MaxTurns = s.MaxTurns, ExtraArgs = s.ExtraArgs, PromptTemplate = s.PromptTemplate,
                AppendSystemPrompt = s.AppendSystemPrompt, PushChanges = s.PushChanges,
                RequiresDecision = s.RequiresDecision, TimeoutMinutes = s.TimeoutMinutes, SystemAction = s.SystemAction,
            };
    }

    /// <summary>Reglas de negocio de una columna. Devuelve todos los errores (campo + mensaje); vacío si es válida.</summary>
    public async Task<List<(string Field, string Message)>> GetValidationErrorsAsync(
        int? id, StageEditViewModel record, CancellationToken ct)
    {
        var others = await GetOthersAsync(id, ct);
        var errors = new List<(string Field, string Message)>();

        if (!PermissionModes.ALL.Contains(record.PermissionMode))
            errors.Add(("Input.PermissionMode", "Modo de permisos inválido."));
        if (record.SuccessStageId is { } ok && !others.Any(s => s.Id == ok))
            errors.Add(("Input.SuccessStageId", "Columna destino inválida."));
        if (record.RejectStageId is { } ko && !others.Any(s => s.Id == ko))
            errors.Add(("Input.RejectStageId", "Columna de rechazo inválida."));
        if (record.Kind == StageKind.System && record.SystemAction == SystemAction.None)
            errors.Add(("Input.SystemAction", "Elige la acción de sistema."));
        if (record.RequiresDecision && record.RejectStageId is null)
            errors.Add(("Input.RejectStageId", "Una etapa con veredicto necesita columna de rechazo."));

        var badArg = Lines(record.ExtraArgs).FirstOrDefault(a => a.Contains(' ') && a.StartsWith("--"));
        if (badArg is not null)
            errors.Add(("Input.ExtraArgs", $"Escribe un argumento por línea («{badArg}» debe ir en dos líneas)."));

        return errors;
    }

    /// <summary>Crea (id nulo) o actualiza una columna. Devuelve false si la columna a editar no existe.</summary>
    public async Task<bool> SaveAsync(int? id, StageEditViewModel record, CancellationToken ct)
    {
        var errors = await GetValidationErrorsAsync(id, record, ct);
        if (errors.Count > 0) throw new ValidationFailedException(errors);

        Stage stage;
        if (id is null)
        {
            stage = new Stage { Order = (await db.Stages.MaxAsync(s => (int?)s.Order, ct) ?? 0) + 10 };
            db.Stages.Add(stage);
        }
        else
        {
            var existing = await db.Stages.FindAsync([id.Value], ct);
            if (existing is null) return false;
            stage = existing;
        }

        stage.Name = record.Name.Trim();
        stage.Icon = string.IsNullOrWhiteSpace(record.Icon) ? "📋" : record.Icon.Trim();
        stage.Kind = record.Kind;
        stage.Description = record.Description;
        stage.SuccessStageId = record.SuccessStageId;
        stage.RejectStageId = record.RejectStageId;
        stage.Model = string.IsNullOrWhiteSpace(record.Model) ? null : record.Model.Trim();
        stage.PermissionMode = record.PermissionMode;
        stage.AllowedTools = record.AllowedTools;
        stage.DisallowedTools = record.DisallowedTools;
        stage.MaxTurns = record.MaxTurns;
        stage.ExtraArgs = record.ExtraArgs;
        stage.PromptTemplate = record.PromptTemplate;
        stage.AppendSystemPrompt = record.AppendSystemPrompt;
        stage.PushChanges = record.Kind == StageKind.Agent && record.PushChanges;
        stage.RequiresDecision = record.Kind == StageKind.Agent && record.RequiresDecision;
        stage.TimeoutMinutes = record.TimeoutMinutes;
        stage.SystemAction = record.Kind == StageKind.System ? record.SystemAction : SystemAction.None;

        if (record.IsInitial)
        {
            await db.Stages.Where(s => s.IsInitial).ForEachAsync(s => s.IsInitial = false, ct);
            stage.IsInitial = true;
        }
        else
        {
            stage.IsInitial = false;
        }

        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Sube (direction &lt; 0) o baja (direction &gt; 0) una columna en el orden del tablero.</summary>
    public async Task MoveAsync(int id, int direction, CancellationToken ct)
    {
        var stages = await db.Stages.OrderBy(s => s.Order).ToListAsync(ct);
        var index = stages.FindIndex(s => s.Id == id);
        var other = index + Math.Sign(direction);
        if (index < 0 || other < 0 || other >= stages.Count) return;

        (stages[index], stages[other]) = (stages[other], stages[index]);
        for (var i = 0; i < stages.Count; i++) stages[i].Order = (i + 1) * 10;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        if (await db.Tasks.AnyAsync(t => t.StageId == id, ct))
            throw new ValidationFailedException(nameof(id),
                "No se puede eliminar una columna que tiene tareas (incluidas las archivadas). Muévelas primero.");
        if (await db.TaskRuns.AnyAsync(r => r.StageId == id, ct))
            throw new ValidationFailedException(nameof(id),
                "La columna tiene historial de ejecuciones; en lugar de eliminarla, déjala sin tareas o renómbrala.");

        var stage = await db.Stages.FindAsync([id], ct);
        if (stage is null) return;
        db.Stages.Remove(stage);
        await db.SaveChangesAsync(ct);
    }

    private static IEnumerable<string> Lines(string? text) =>
        (text ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
