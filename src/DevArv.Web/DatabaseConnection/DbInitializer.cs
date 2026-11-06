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

        // Estas dos adaptaciones usan los nombres antiguos de tabla, así que van antes del renombrado.
        await AddStageSkillColumnAsync(db, CT);
        await AddMergeColumnsAsync(db, CT);
        await AddConflictColumnsAsync(db, CT);
        await RenameTablesToModuleNamesAsync(db, CT);

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
    /// No hace nada si la columna ya existe o si la base ya usa los nombres de tabla por módulo.
    /// </summary>
    private static Task AddStageSkillColumnAsync(AppDbContext Db, CancellationToken CT) =>
        Db.Database.ExecuteSqlRawAsync(ADD_STAGE_SKILL_COLUMN_SQL, CT);

    // Sin llaves en el SQL final: EF Core lo pasa por string.Format.
    internal const string ADD_STAGE_SKILL_COLUMN_SQL = $"""
        DO $$
        BEGIN
            IF EXISTS (SELECT 1 FROM information_schema.tables
                       WHERE table_schema = current_schema() AND table_name = 'Stages')
               AND NOT EXISTS (SELECT 1 FROM information_schema.columns
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
    /// No hace nada si las columnas ya existen o si la base ya usa los nombres de tabla por módulo.
    /// </summary>
    private static Task AddMergeColumnsAsync(AppDbContext Db, CancellationToken CT) =>
        Db.Database.ExecuteSqlRawAsync(ADD_MERGE_COLUMNS_SQL, CT);

    // Sin llaves en el SQL final: EF Core lo pasa por string.Format.
    internal const string ADD_MERGE_COLUMNS_SQL = $"""
        DO $$
        BEGIN
            IF EXISTS (SELECT 1 FROM information_schema.tables
                       WHERE table_schema = current_schema() AND table_name = 'Stages')
               AND NOT EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_schema = current_schema() AND table_name = 'Stages' AND column_name = 'IsMergeDestination') THEN
                ALTER TABLE "Stages" ADD COLUMN "IsMergeDestination" boolean NOT NULL DEFAULT FALSE;
                UPDATE "Stages" SET "IsMergeDestination" = TRUE
                WHERE "Id" = (SELECT "Id" FROM "Stages" WHERE "Name" = '{PENDING_DEPLOY_STAGE_NAME}' ORDER BY "Order" LIMIT 1);
            END IF;
            IF EXISTS (SELECT 1 FROM information_schema.tables
                       WHERE table_schema = current_schema() AND table_name = 'Tasks')
               AND NOT EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_schema = current_schema() AND table_name = 'Tasks' AND column_name = 'PullRequestMergedAt') THEN
                ALTER TABLE "Tasks" ADD COLUMN "PullRequestMergedAt" timestamp with time zone NULL;
            END IF;
        END $$;
        """;

    /// <summary>
    /// Nombre antiguo y nuevo (<c>Modulo_Tabla</c>) de cada tabla. Las bases ya creadas conservan los datos: sólo
    /// se renombra la tabla. Las columnas y los valores de texto de los enums no cambian (ver
    /// <see cref="AppDbContext.ToColumnName"/> y <see cref="EnumNameStringConverter{TEnum}"/>).
    /// </summary>
    internal static readonly (string OldName, string NewName)[] TABLE_RENAMES =
    [
        ("Projects", TableNames.PROJECTS),
        ("Stages", TableNames.STAGES),
        ("Tasks", TableNames.TASKS),
        ("TaskAttachments", TableNames.TASK_ATTACHMENTS),
        ("TaskRuns", TableNames.TASK_RUNS),
        ("TaskComments", TableNames.TASK_COMMENTS),
        ("TaskEvents", TableNames.TASK_EVENTS),
        ("Workers", TableNames.WORKERS),
        ("AspNetUsers", TableNames.IDENTITY_USERS),
        ("AspNetRoles", TableNames.IDENTITY_ROLES),
        ("AspNetUserRoles", TableNames.IDENTITY_USER_ROLES),
        ("AspNetUserClaims", TableNames.IDENTITY_USER_CLAIMS),
        ("AspNetUserLogins", TableNames.IDENTITY_USER_LOGINS),
        ("AspNetUserTokens", TableNames.IDENTITY_USER_TOKENS),
        ("AspNetRoleClaims", TableNames.IDENTITY_ROLE_CLAIMS),
    ];

    /// <summary>
    /// Bases creadas antes de nombrar las tablas por módulo: renombra cada tabla antigua. Va en un solo bloque, así
    /// que se aplica completo o no se aplica. No hace nada si la tabla antigua no existe (base nueva o ya renombrada).
    /// </summary>
    private static Task RenameTablesToModuleNamesAsync(AppDbContext Db, CancellationToken CT) =>
        Db.Database.ExecuteSqlRawAsync(BuildRenameTablesSql(), CT);

    internal static string BuildRenameTablesSql()
    {
        var steps = TABLE_RENAMES.Select(rename => $"""
            IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = current_schema() AND table_name = '{rename.OldName}')
               AND NOT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = current_schema() AND table_name = '{rename.NewName}') THEN
                ALTER TABLE "{rename.OldName}" RENAME TO "{rename.NewName}";
            END IF;
""");
        return "DO $$\nBEGIN\n" + string.Join("\n", steps) + "\nEND $$;";
    }

    private const string PENDING_DEPLOY_STAGE_NAME = "Pendiente Deploy";

    /// <summary>
    /// Bases creadas antes de detectar conflictos en los pull requests: agrega <see cref="TaskItem.PullRequestHasConflicts"/>
    /// y <see cref="TaskRun.ResolveConflicts"/>. No hace nada si las columnas ya existen o si la base ya usa los nombres
    /// de tabla por módulo.
    /// </summary>
    private static Task AddConflictColumnsAsync(AppDbContext Db, CancellationToken CT) =>
        Db.Database.ExecuteSqlRawAsync(ADD_CONFLICT_COLUMNS_SQL, CT);

    // Sin llaves en el SQL: EF Core lo pasa por string.Format.
    internal const string ADD_CONFLICT_COLUMNS_SQL = """
        DO $$
        BEGIN
            IF EXISTS (SELECT 1 FROM information_schema.tables
                       WHERE table_schema = current_schema() AND table_name = 'Tasks')
               AND NOT EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_schema = current_schema() AND table_name = 'Tasks' AND column_name = 'PullRequestHasConflicts') THEN
                ALTER TABLE "Tasks" ADD COLUMN "PullRequestHasConflicts" boolean NOT NULL DEFAULT FALSE;
            END IF;
            IF EXISTS (SELECT 1 FROM information_schema.tables
                       WHERE table_schema = current_schema() AND table_name = 'TaskRuns')
               AND NOT EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_schema = current_schema() AND table_name = 'TaskRuns' AND column_name = 'ResolveConflicts') THEN
                ALTER TABLE "TaskRuns" ADD COLUMN "ResolveConflicts" boolean NOT NULL DEFAULT FALSE;
            END IF;
        END $$;
        """;

    public static async Task SeedDefaultWorkflowAsync(AppDbContext Db, CancellationToken CT)
    {
        var backlog = new Stage { Name = "Backlog", Icon = "📥", Kind = StageKind.MANUAL, IsInitial = true,
            Description = "Tareas nuevas. Muévela a «Aprobación Tarea» cuando esté lista." };
        var improve = new Stage { Name = "Requiere Mejoras", Icon = "✏️", Kind = StageKind.MANUAL,
            Description = "La descripción necesita más detalle antes de desarrollarse." };
        var approveTask = new Stage { Name = "Aprobación Tarea", Icon = "✅", Kind = StageKind.HUMAN_APPROVAL,
            Description = "Una persona confirma que la tarea está bien definida." };
        var developer = new Stage
        {
            Name = "Desarrollador", Icon = "💻", Kind = StageKind.AGENT, Model = "opus",
            PermissionMode = "acceptEdits", AllowedTools = DefaultPrompts.DEVELOPER_ALLOWED_TOOLS,
            PromptTemplate = DefaultPrompts.DEVELOPER, PushChanges = true, TimeoutMinutes = 90,
            Description = "Claude implementa la tarea en una rama y hace push.",
        };
        var reviewer = new Stage
        {
            Name = "Revisor de Código", Icon = "🔍", Kind = StageKind.AGENT, Model = "sonnet",
            PermissionMode = "default", AllowedTools = DefaultPrompts.REVIEWER_ALLOWED_TOOLS,
            DisallowedTools = DefaultPrompts.REVIEWER_DISALLOWED_TOOLS, PromptTemplate = DefaultPrompts.REVIEWER,
            Skill = DefaultPrompts.REVIEWER_SKILL, RequiresDecision = true, TimeoutMinutes = 30,
            Description = "Claude revisa el diff y aprueba o devuelve al desarrollador.",
        };
        var prCreator = new Stage { Name = "Creador de PR", Icon = "🔀", Kind = StageKind.SYSTEM,
            SystemAction = SystemAction.CREATE_PULL_REQUEST, Description = "Abre el pull request en GitHub." };
        var productApproval = new Stage { Name = "Aprobación Producto", Icon = "🧑‍💼", Kind = StageKind.HUMAN_APPROVAL,
            Description = "Producto valida el resultado. «Pedir cambios» devuelve al desarrollador." };
        var manualReview = new Stage { Name = "Revisión Código Manual", Icon = "🧐", Kind = StageKind.HUMAN_APPROVAL,
            Description = "Un desarrollador revisa el PR en GitHub." };
        var pendingDeploy = new Stage { Name = PENDING_DEPLOY_STAGE_NAME, Icon = "🚀", Kind = StageKind.MANUAL,
            IsMergeDestination = true, Description = "La tarea entra sola aquí cuando se hace merge de su pull request." };
        var pendingTest = new Stage { Name = "Pendiente Prueba", Icon = "🧪", Kind = StageKind.MANUAL };
        var done = new Stage { Name = "Terminado", Icon = "🏁", Kind = StageKind.TERMINAL };
        var discarded = new Stage { Name = "Descartado", Icon = "🗑️", Kind = StageKind.TERMINAL };

        Stage[] all = [backlog, improve, approveTask, developer, reviewer, prCreator, productApproval,
            manualReview, pendingDeploy, pendingTest, done, discarded];
        for (var i = 0; i < all.Length; i++) all[i].Order = (i + 1) * 10;

        Db.Stages.AddRange(all);
        await Db.SaveChangesAsync(CT);

        backlog.SuccessStageID = approveTask.ID;
        improve.SuccessStageID = approveTask.ID;
        approveTask.SuccessStageID = developer.ID;
        approveTask.RejectStageID = improve.ID;
        developer.SuccessStageID = reviewer.ID;
        reviewer.SuccessStageID = prCreator.ID;
        reviewer.RejectStageID = developer.ID;
        prCreator.SuccessStageID = productApproval.ID;
        productApproval.SuccessStageID = manualReview.ID;
        productApproval.RejectStageID = developer.ID;
        manualReview.SuccessStageID = pendingDeploy.ID;
        manualReview.RejectStageID = developer.ID;
        pendingDeploy.SuccessStageID = pendingTest.ID;
        pendingTest.SuccessStageID = done.ID;
        await Db.SaveChangesAsync(CT);
    }
}
