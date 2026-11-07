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
        // Estas usan el nombre nuevo de la tabla, así que van después del renombrado (también aplican a bases ya renombradas).
        await AddCaptureColumnAsync(db, CT);
        await AddAttachmentContentColumnAsync(db, CT);
        await AllowNullLegacyStoredNameAsync(db, CT);
        await MoveLegacyAttachmentsToDatabaseAsync(
            Db: db,
            Folder: Path.Combine(
                Services.GetRequiredService<IWebHostEnvironment>().ContentRootPath,
                Services.GetRequiredService<IOptions<AttachmentOptions>>().Value.StoragePath),
            Logger: logger,
            CT: CT);

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

    /// <summary>
    /// Bases creadas antes de la pestaña «Capturas» del detalle: agrega <see cref="TaskAttachment.IsCapture"/>.
    /// No hace nada si la columna ya existe o si la tabla no existe. Debe correr después de <see cref="RenameTablesToModuleNamesAsync"/>.
    /// </summary>
    private static Task AddCaptureColumnAsync(AppDbContext Db, CancellationToken CT) =>
        Db.Database.ExecuteSqlRawAsync(ADD_CAPTURE_COLUMN_SQL, CT);

    // Sin llaves en el SQL: EF Core lo pasa por string.Format.
    internal static readonly string ADD_CAPTURE_COLUMN_SQL = $"""
        DO $$
        BEGIN
            IF EXISTS (SELECT 1 FROM information_schema.tables
                       WHERE table_schema = current_schema() AND table_name = '{TableNames.TASK_ATTACHMENTS}')
               AND NOT EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_schema = current_schema() AND table_name = '{TableNames.TASK_ATTACHMENTS}' AND column_name = 'IsCapture') THEN
                ALTER TABLE "{TableNames.TASK_ATTACHMENTS}" ADD COLUMN "IsCapture" boolean NOT NULL DEFAULT FALSE;
            END IF;
        END $$;
        """;

    /// <summary>
    /// Bases creadas cuando los adjuntos se guardaban en disco: agrega la columna <c>Content</c>
    /// (<see cref="TaskAttachmentContent.Data"/>). No hace nada si la columna ya existe o si la tabla no existe.
    /// Debe correr después de <see cref="RenameTablesToModuleNamesAsync"/>.
    /// </summary>
    private static Task AddAttachmentContentColumnAsync(AppDbContext Db, CancellationToken CT) =>
        Db.Database.ExecuteSqlRawAsync(ADD_ATTACHMENT_CONTENT_COLUMN_SQL, CT);

    // Sin llaves en el SQL: EF Core lo pasa por string.Format.
    internal static readonly string ADD_ATTACHMENT_CONTENT_COLUMN_SQL = $"""
        DO $$
        BEGIN
            IF EXISTS (SELECT 1 FROM information_schema.tables
                       WHERE table_schema = current_schema() AND table_name = '{TableNames.TASK_ATTACHMENTS}')
               AND NOT EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_schema = current_schema() AND table_name = '{TableNames.TASK_ATTACHMENTS}' AND column_name = '{AppDbContext.ATTACHMENT_CONTENT_COLUMN}') THEN
                ALTER TABLE "{TableNames.TASK_ATTACHMENTS}" ADD COLUMN "{AppDbContext.ATTACHMENT_CONTENT_COLUMN}" bytea NULL;
            END IF;
        END $$;
        """;

    /// <summary>
    /// Bases creadas cuando los adjuntos se guardaban en disco: la columna <c>StoredName</c> era NOT NULL y el modelo ya no
    /// la llena, así que mientras se conserve (ver <see cref="MoveLegacyAttachmentsToDatabaseAsync"/>) cualquier adjunto
    /// nuevo fallaría con «null value in column "StoredName"». Le quita la restricción NOT NULL; las filas antiguas
    /// conservan su valor. No hace nada si la tabla o la columna no existen o si ya admite nulos.
    /// Debe correr después de <see cref="RenameTablesToModuleNamesAsync"/>.
    /// </summary>
    private static Task AllowNullLegacyStoredNameAsync(AppDbContext Db, CancellationToken CT) =>
        Db.Database.ExecuteSqlRawAsync(ALLOW_NULL_LEGACY_STORED_NAME_SQL, CT);

    // Sin llaves en el SQL: EF Core lo pasa por string.Format.
    internal static readonly string ALLOW_NULL_LEGACY_STORED_NAME_SQL = $"""
        DO $$
        BEGIN
            IF EXISTS (SELECT 1 FROM information_schema.columns
                       WHERE table_schema = current_schema() AND table_name = '{TableNames.TASK_ATTACHMENTS}'
                         AND column_name = '{LEGACY_STORED_NAME_COLUMN}' AND is_nullable = 'NO') THEN
                ALTER TABLE "{TableNames.TASK_ATTACHMENTS}" ALTER COLUMN "{LEGACY_STORED_NAME_COLUMN}" DROP NOT NULL;
            END IF;
        END $$;
        """;

    /// <summary>Columna de las bases antiguas con el nombre en disco de cada adjunto; ya no está en el modelo.</summary>
    internal const string LEGACY_STORED_NAME_COLUMN = "StoredName";

    /// <summary>
    /// Bases creadas cuando los adjuntos se guardaban en disco (columna <c>StoredName</c>): copia cada archivo a la columna
    /// <c>Content</c> y, ya confirmada la transacción, borra los archivos copiados.
    /// La columna <c>StoredName</c> es lo único que relaciona cada fila con su archivo, así que sólo se quita cuando
    /// TODOS los archivos se copiaron. Si alguno no se encuentra (carpeta equivocada, archivos restaurados después…) se
    /// conserva la columna y se reintenta en el siguiente arranque con las filas que sigan sin contenido.
    /// No hace nada si la columna <c>StoredName</c> no existe. Debe correr después de <see cref="AddAttachmentContentColumnAsync"/>.
    /// </summary>
    private static async Task MoveLegacyAttachmentsToDatabaseAsync(
        AppDbContext Db, string Folder, ILogger Logger, CancellationToken CT)
    {
        var hasStoredName = await Db.Database.SqlQueryRaw<int>($"""
            SELECT COUNT(*)::int AS "Value" FROM information_schema.columns
            WHERE table_schema = current_schema() AND table_name = '{TableNames.TASK_ATTACHMENTS}' AND column_name = '{LEGACY_STORED_NAME_COLUMN}'
            """).SingleAsync(CT) > 0;
        if (!hasStoredName) return;

        var rows = await Db.Database.SqlQueryRaw<LegacyAttachmentRow>($"""
            SELECT "Id" AS "ID", "StoredName" FROM "{TableNames.TASK_ATTACHMENTS}"
            WHERE "{AppDbContext.ATTACHMENT_CONTENT_COLUMN}" IS NULL
            """).ToListAsync(CT);

        var copied = new List<string>();
        var missing = 0;
        await using (var transaction = await Db.Database.BeginTransactionAsync(CT))
        {
            foreach (var row in rows)
            {
                var path = ResolveLegacyPath(
                    Folder: Folder,
                    StoredName: row.StoredName);
                if (path is null)
                {
                    missing++;
                    continue;
                }

                var fileBytes = await File.ReadAllBytesAsync(path, CT);
                // El nombre de la tabla no puede ir como parámetro: {0} y {1} son los parámetros de EF Core.
                await Db.Database.ExecuteSqlRawAsync(
                    sql: $$"""UPDATE "{{TableNames.TASK_ATTACHMENTS}}" SET "{{AppDbContext.ATTACHMENT_CONTENT_COLUMN}}" = {0} WHERE "Id" = {1}""",
                    parameters: [fileBytes, row.ID],
                    cancellationToken: CT);
                copied.Add(path);
            }

            if (missing == 0)
                await Db.Database.ExecuteSqlRawAsync($"""ALTER TABLE "{TableNames.TASK_ATTACHMENTS}" DROP COLUMN "StoredName" """, CT);
            await transaction.CommitAsync(CT);
        }

        Logger.LogInformation("Adjuntos movidos del disco a la base de datos: {Copied}.", copied.Count);
        if (missing > 0)
            Logger.LogWarning(
                "{Missing} adjunto(s) no tienen archivo en {Folder}. Se conserva la columna StoredName: restaura la carpeta y reinicia para reintentar.",
                missing, Folder);

        foreach (var path in copied)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Logger.LogWarning(ex, "No se pudo borrar el adjunto antiguo {Path}; ya está en la base de datos.", path);
            }
        }
        if (Directory.Exists(Folder) && !Directory.EnumerateFileSystemEntries(Folder).Any())
            Directory.Delete(Folder);
    }

    /// <summary>
    /// Ruta del archivo antiguo de un adjunto; null si <paramref name="StoredName"/> es null o vacío o el archivo no existe.
    /// <c>StoredName</c> lo generaba el servidor, pero sólo se usa su último segmento para evitar path traversal.
    /// </summary>
    internal static string? ResolveLegacyPath(string Folder, string? StoredName)
    {
        var name = Path.GetFileName(StoredName);
        if (string.IsNullOrEmpty(name)) return null;

        var path = Path.Combine(Folder, name);
        return File.Exists(path) ? path : null;
    }

    /// <summary>Fila de la tabla de adjuntos de una base anterior a guardar el contenido en la base de datos.</summary>
    internal sealed class LegacyAttachmentRow
    {
        public long ID { get; set; }
        /// <summary>Null en las filas creadas después de quitarle el NOT NULL; ésas ya tienen contenido.</summary>
        public string? StoredName { get; set; }
    }

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
