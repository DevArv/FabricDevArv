using DevArv.Web.Constants;
using DevArv.Web.DataModels;
using DevArv.Web.Enums.Workflow;
using DevArv.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DevArv.Web.DatabaseConnection;

public static class DbInitializer
{
    public static async Task InitializeAsync(IServiceProvider Services, CancellationToken CT = default)
    {
        var db = Services.GetRequiredService<AppDbContext>();
        var admin = Services.GetRequiredService<IOptions<AdminOptions>>().Value;
        var logger = Services.GetRequiredService<ILoggerFactory>().CreateLogger("DbInitializer");

        // Si existen migraciones (dotnet ef migrations add ...) se aplican; si no, se crea el esquema directo.
        if (db.Database.GetMigrations().Any())
        {
            await db.Database.MigrateAsync(CT);
        }
        else
        {
            logger.LogWarning("No hay migraciones de EF Core; creando el esquema con EnsureCreated. " +
                              "Ejecuta 'dotnet ef migrations add Initial' antes de pasar a producción.");
            await db.Database.EnsureCreatedAsync(CT);
        }

        await AddStageSkillColumnAsync(db, CT);
        await AddMergeColumnsAsync(db, CT);

        if (!await db.Stages.AnyAsync(CT))
        {
            await SeedDefaultWorkflowAsync(db, CT);
            logger.LogInformation("Flujo por defecto creado.");
        }

        var userManager = Services.GetRequiredService<UserManager<IdentityUser>>();
        if (!string.IsNullOrWhiteSpace(admin.Email) && !string.IsNullOrWhiteSpace(admin.Password)
            && !await userManager.Users.AnyAsync(CT))
        {
            var user = new IdentityUser { UserName = admin.Email, Email = admin.Email, EmailConfirmed = true };
            var result = await userManager.CreateAsync(user, admin.Password);
            if (result.Succeeded)
                logger.LogInformation("Usuario administrador {Email} creado.", admin.Email);
            else
                logger.LogError("No se pudo crear el administrador: {Errors}",
                    string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }

    /// <summary>
    /// Bases creadas antes de existir <see cref="Stage.Skill"/> (EnsureCreated no altera tablas existentes):
    /// agrega la columna y, sólo esa vez, asigna el skill de revisión a las etapas de agente con veredicto.
    /// No hace nada si la columna ya existe.
    /// </summary>
    private static Task AddStageSkillColumnAsync(AppDbContext Db, CancellationToken CT) =>
        Db.Database.ExecuteSqlRawAsync(ADD_STAGE_SKILL_COLUMN_SQL, CT);

    // Sin llaves en el SQL final: EF Core lo pasa por string.Format.
    private const string ADD_STAGE_SKILL_COLUMN_SQL = $"""
        DO $$
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                           WHERE table_schema = current_schema() AND table_name = 'Stages' AND column_name = 'Skill') THEN
                ALTER TABLE "Stages" ADD COLUMN "Skill" character varying(100) NULL;
                UPDATE "Stages" SET "Skill" = '{DefaultPrompts.REVIEWER_SKILL}'
                WHERE "Kind" = 'Agent' AND "RequiresDecision";
            END IF;
        END $$;
        """;

    /// <summary>
    /// Bases creadas antes de mover tareas al hacer merge del PR: agrega <see cref="Stage.IsMergeDestination"/>
    /// (y, sólo esa vez, la marca en la columna «Pendiente Deploy» si existe) y <see cref="TaskItem.PullRequestMergedAt"/>.
    /// No hace nada si las columnas ya existen.
    /// </summary>
    private static Task AddMergeColumnsAsync(AppDbContext Db, CancellationToken CT) =>
        Db.Database.ExecuteSqlRawAsync(ADD_MERGE_COLUMNS_SQL, CT);

    // Sin llaves en el SQL final: EF Core lo pasa por string.Format.
    private const string ADD_MERGE_COLUMNS_SQL = $"""
        DO $$
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                           WHERE table_schema = current_schema() AND table_name = 'Stages' AND column_name = 'IsMergeDestination') THEN
                ALTER TABLE "Stages" ADD COLUMN "IsMergeDestination" boolean NOT NULL DEFAULT FALSE;
                UPDATE "Stages" SET "IsMergeDestination" = TRUE
                WHERE "Id" = (SELECT "Id" FROM "Stages" WHERE "Name" = '{PENDING_DEPLOY_STAGE_NAME}' ORDER BY "Order" LIMIT 1);
            END IF;
            IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                           WHERE table_schema = current_schema() AND table_name = 'Tasks' AND column_name = 'PullRequestMergedAt') THEN
                ALTER TABLE "Tasks" ADD COLUMN "PullRequestMergedAt" timestamp with time zone NULL;
            END IF;
        END $$;
        """;

    private const string PENDING_DEPLOY_STAGE_NAME = "Pendiente Deploy";

    public static async Task SeedDefaultWorkflowAsync(AppDbContext Db, CancellationToken CT)
    {
        var backlog = new Stage { Name = "Backlog", Icon = "📥", Kind = StageKind.Manual, IsInitial = true,
            Description = "Tareas nuevas. Muévela a «Aprobación Tarea» cuando esté lista." };
        var improve = new Stage { Name = "Requiere Mejoras", Icon = "✏️", Kind = StageKind.Manual,
            Description = "La descripción necesita más detalle antes de desarrollarse." };
        var approveTask = new Stage { Name = "Aprobación Tarea", Icon = "✅", Kind = StageKind.HumanApproval,
            Description = "Una persona confirma que la tarea está bien definida." };
        var developer = new Stage
        {
            Name = "Desarrollador", Icon = "💻", Kind = StageKind.Agent, Model = "opus",
            PermissionMode = "acceptEdits", AllowedTools = DefaultPrompts.DEVELOPER_ALLOWED_TOOLS,
            PromptTemplate = DefaultPrompts.DEVELOPER, PushChanges = true, TimeoutMinutes = 90,
            Description = "Claude implementa la tarea en una rama y hace push.",
        };
        var reviewer = new Stage
        {
            Name = "Revisor de Código", Icon = "🔍", Kind = StageKind.Agent, Model = "sonnet",
            PermissionMode = "default", AllowedTools = DefaultPrompts.REVIEWER_ALLOWED_TOOLS,
            DisallowedTools = DefaultPrompts.REVIEWER_DISALLOWED_TOOLS, PromptTemplate = DefaultPrompts.REVIEWER,
            Skill = DefaultPrompts.REVIEWER_SKILL, RequiresDecision = true, TimeoutMinutes = 30,
            Description = "Claude revisa el diff y aprueba o devuelve al desarrollador.",
        };
        var prCreator = new Stage { Name = "Creador de PR", Icon = "🔀", Kind = StageKind.System,
            SystemAction = SystemAction.CreatePullRequest, Description = "Abre el pull request en GitHub." };
        var productApproval = new Stage { Name = "Aprobación Producto", Icon = "🧑‍💼", Kind = StageKind.HumanApproval,
            Description = "Producto valida el resultado. «Pedir cambios» devuelve al desarrollador." };
        var manualReview = new Stage { Name = "Revisión Código Manual", Icon = "🧐", Kind = StageKind.HumanApproval,
            Description = "Un desarrollador revisa el PR en GitHub." };
        var pendingDeploy = new Stage { Name = PENDING_DEPLOY_STAGE_NAME, Icon = "🚀", Kind = StageKind.Manual,
            IsMergeDestination = true, Description = "La tarea entra sola aquí cuando se hace merge de su pull request." };
        var pendingTest = new Stage { Name = "Pendiente Prueba", Icon = "🧪", Kind = StageKind.Manual };
        var done = new Stage { Name = "Terminado", Icon = "🏁", Kind = StageKind.Terminal };
        var discarded = new Stage { Name = "Descartado", Icon = "🗑️", Kind = StageKind.Terminal };

        Stage[] all = [backlog, improve, approveTask, developer, reviewer, prCreator, productApproval,
            manualReview, pendingDeploy, pendingTest, done, discarded];
        for (var i = 0; i < all.Length; i++) all[i].Order = (i + 1) * 10;

        Db.Stages.AddRange(all);
        await Db.SaveChangesAsync(CT);

        backlog.SuccessStageId = approveTask.Id;
        improve.SuccessStageId = approveTask.Id;
        approveTask.SuccessStageId = developer.Id;
        approveTask.RejectStageId = improve.Id;
        developer.SuccessStageId = reviewer.Id;
        reviewer.SuccessStageId = prCreator.Id;
        reviewer.RejectStageId = developer.Id;
        prCreator.SuccessStageId = productApproval.Id;
        productApproval.SuccessStageId = manualReview.Id;
        productApproval.RejectStageId = developer.Id;
        manualReview.SuccessStageId = pendingDeploy.Id;
        manualReview.RejectStageId = developer.Id;
        pendingDeploy.SuccessStageId = pendingTest.Id;
        pendingTest.SuccessStageId = done.Id;
        await Db.SaveChangesAsync(CT);
    }
}
