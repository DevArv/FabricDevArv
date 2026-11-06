using DevArv.Web.Constants;
using DevArv.Web.DataModels;
using DevArv.Web.Enums.Workflow;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DevArv.Web.DatabaseConnection;

public static class DbInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var config = services.GetRequiredService<IConfiguration>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("DbInitializer");

        // Si existen migraciones (dotnet ef migrations add ...) se aplican; si no, se crea el esquema directo.
        if (db.Database.GetMigrations().Any())
        {
            await db.Database.MigrateAsync(ct);
        }
        else
        {
            logger.LogWarning("No hay migraciones de EF Core; creando el esquema con EnsureCreated. " +
                              "Ejecuta 'dotnet ef migrations add Initial' antes de pasar a producción.");
            await db.Database.EnsureCreatedAsync(ct);
        }

        await AddStageSkillColumnAsync(db, ct);
        await AddMergeColumnsAsync(db, ct);
        await AddConflictColumnsAsync(db, ct);
        await AddAttachmentContentColumnAsync(db, ct);
        var legacyAttachmentsDir = Path.Combine(
            services.GetRequiredService<IWebHostEnvironment>().ContentRootPath,
            config["Attachments:StoragePath"] is { Length: > 0 } storagePath ? storagePath : LegacyAttachmentsPath);
        await MoveLegacyAttachmentsToDatabaseAsync(db, legacyAttachmentsDir, logger, ct);

        if (!await db.Stages.AnyAsync(ct))
        {
            await SeedDefaultWorkflowAsync(db, ct);
            logger.LogInformation("Flujo por defecto creado.");
        }

        var userManager = services.GetRequiredService<UserManager<IdentityUser>>();
        var adminEmail = config["Admin:Email"];
        var adminPassword = config["Admin:Password"];
        if (!string.IsNullOrWhiteSpace(adminEmail) && !string.IsNullOrWhiteSpace(adminPassword)
            && !await userManager.Users.AnyAsync(ct))
        {
            var admin = new IdentityUser { UserName = adminEmail, Email = adminEmail, EmailConfirmed = true };
            var result = await userManager.CreateAsync(admin, adminPassword);
            if (result.Succeeded)
                logger.LogInformation("Usuario administrador {Email} creado.", adminEmail);
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
    private static Task AddStageSkillColumnAsync(AppDbContext db, CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync(AddStageSkillColumnSql, ct);

    // Sin llaves en el SQL final: EF Core lo pasa por string.Format.
    private const string AddStageSkillColumnSql = $"""
        DO $$
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                           WHERE table_schema = current_schema() AND table_name = 'Stages' AND column_name = 'Skill') THEN
                ALTER TABLE "Stages" ADD COLUMN "Skill" character varying(100) NULL;
                UPDATE "Stages" SET "Skill" = '{DefaultPrompts.ReviewerSkill}'
                WHERE "Kind" = 'Agent' AND "RequiresDecision";
            END IF;
        END $$;
        """;

    /// <summary>
    /// Bases creadas antes de mover tareas al hacer merge del PR: agrega <see cref="Stage.IsMergeDestination"/>
    /// (y, sólo esa vez, la marca en la columna «Pendiente Deploy» si existe) y <see cref="TaskItem.PullRequestMergedAt"/>.
    /// No hace nada si las columnas ya existen.
    /// </summary>
    private static Task AddMergeColumnsAsync(AppDbContext db, CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync(AddMergeColumnsSql, ct);

    // Sin llaves en el SQL final: EF Core lo pasa por string.Format.
    private const string AddMergeColumnsSql = $"""
        DO $$
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                           WHERE table_schema = current_schema() AND table_name = 'Stages' AND column_name = 'IsMergeDestination') THEN
                ALTER TABLE "Stages" ADD COLUMN "IsMergeDestination" boolean NOT NULL DEFAULT FALSE;
                UPDATE "Stages" SET "IsMergeDestination" = TRUE
                WHERE "Id" = (SELECT "Id" FROM "Stages" WHERE "Name" = '{PendingDeployStageName}' ORDER BY "Order" LIMIT 1);
            END IF;
            IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                           WHERE table_schema = current_schema() AND table_name = 'Tasks' AND column_name = 'PullRequestMergedAt') THEN
                ALTER TABLE "Tasks" ADD COLUMN "PullRequestMergedAt" timestamp with time zone NULL;
            END IF;
        END $$;
        """;

    private const string PendingDeployStageName = "Pendiente Deploy";

    /// <summary>
    /// Bases creadas antes de detectar conflictos en los pull requests: agrega <see cref="TaskItem.PullRequestHasConflicts"/>
    /// y <see cref="TaskRun.ResolveConflicts"/>. No hace nada si las columnas ya existen.
    /// </summary>
    private static Task AddConflictColumnsAsync(AppDbContext db, CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync(AddConflictColumnsSql, ct);

    // Sin llaves en el SQL: EF Core lo pasa por string.Format.
    private const string AddConflictColumnsSql = """
        DO $$
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                           WHERE table_schema = current_schema() AND table_name = 'Tasks' AND column_name = 'PullRequestHasConflicts') THEN
                ALTER TABLE "Tasks" ADD COLUMN "PullRequestHasConflicts" boolean NOT NULL DEFAULT FALSE;
            END IF;
            IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                           WHERE table_schema = current_schema() AND table_name = 'TaskRuns' AND column_name = 'ResolveConflicts') THEN
                ALTER TABLE "TaskRuns" ADD COLUMN "ResolveConflicts" boolean NOT NULL DEFAULT FALSE;
            END IF;
        END $$;
        """;

    /// <summary>
    /// Bases creadas cuando los adjuntos se guardaban en disco: agrega la columna <c>TaskAttachments.Content</c>
    /// (<see cref="TaskAttachmentContent.Data"/>). No hace nada si la columna ya existe.
    /// </summary>
    private static Task AddAttachmentContentColumnAsync(AppDbContext db, CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync(AddAttachmentContentColumnSql, ct);

    // Sin llaves en el SQL: EF Core lo pasa por string.Format.
    private const string AddAttachmentContentColumnSql = """
        DO $$
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                           WHERE table_schema = current_schema() AND table_name = 'TaskAttachments' AND column_name = 'Content') THEN
                ALTER TABLE "TaskAttachments" ADD COLUMN "Content" bytea NULL;
            END IF;
        END $$;
        """;

    /// <summary>Carpeta (relativa a la raíz del proyecto web) donde antes se guardaban los adjuntos.</summary>
    private const string LegacyAttachmentsPath = "App_Data/attachments";

    /// <summary>
    /// Bases creadas cuando los adjuntos se guardaban en disco (columna <c>StoredName</c>): copia cada archivo a la columna
    /// <c>Content</c>, quita la columna <c>StoredName</c> y, ya confirmada la transacción, borra los archivos copiados.
    /// Un adjunto cuyo archivo ya no existe queda sin contenido (no se puede descargar). No hace nada si la columna
    /// <c>StoredName</c> no existe.
    /// </summary>
    private static async Task MoveLegacyAttachmentsToDatabaseAsync(
        AppDbContext db, string folder, ILogger logger, CancellationToken ct)
    {
        var hasStoredName = await db.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*)::int AS "Value" FROM information_schema.columns
            WHERE table_schema = current_schema() AND table_name = 'TaskAttachments' AND column_name = 'StoredName'
            """).SingleAsync(ct) > 0;
        if (!hasStoredName) return;

        var rows = await db.Database.SqlQueryRaw<LegacyAttachmentRow>(
            """SELECT "Id", "StoredName" FROM "TaskAttachments" WHERE "Content" IS NULL""").ToListAsync(ct);

        var copied = new List<string>();
        var missing = 0;
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            foreach (var row in rows)
            {
                // StoredName lo generaba el servidor, pero se valida igual para evitar path traversal.
                var path = Path.Combine(folder, Path.GetFileName(row.StoredName));
                if (row.StoredName.Length == 0 || !File.Exists(path))
                {
                    missing++;
                    continue;
                }

                var data = await File.ReadAllBytesAsync(path, ct);
                await db.Database.ExecuteSqlAsync(
                    $"""UPDATE "TaskAttachments" SET "Content" = {data} WHERE "Id" = {row.Id}""", ct);
                copied.Add(path);
            }

            await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "TaskAttachments" DROP COLUMN "StoredName" """, ct);
            await transaction.CommitAsync(ct);
        }

        logger.LogInformation("Adjuntos movidos del disco a la base de datos: {Copied}.", copied.Count);
        if (missing > 0)
            logger.LogWarning("{Missing} adjunto(s) no tenían archivo en {Folder}; quedan sin contenido.", missing, folder);

        foreach (var path in copied)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "No se pudo borrar el adjunto antiguo {Path}; ya está en la base de datos.", path);
            }
        }
        if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
            Directory.Delete(folder);
    }

    /// <summary>Fila de <c>TaskAttachments</c> de una base anterior a guardar el contenido en la base de datos.</summary>
    internal sealed class LegacyAttachmentRow
    {
        public long Id { get; set; }
        public string StoredName { get; set; } = "";
    }

    public static async Task SeedDefaultWorkflowAsync(AppDbContext db, CancellationToken ct)
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
            PermissionMode = "acceptEdits", AllowedTools = DefaultPrompts.DeveloperAllowedTools,
            PromptTemplate = DefaultPrompts.Developer, PushChanges = true, TimeoutMinutes = 90,
            Description = "Claude implementa la tarea en una rama y hace push.",
        };
        var reviewer = new Stage
        {
            Name = "Revisor de Código", Icon = "🔍", Kind = StageKind.Agent, Model = "sonnet",
            PermissionMode = "default", AllowedTools = DefaultPrompts.ReviewerAllowedTools,
            DisallowedTools = DefaultPrompts.ReviewerDisallowedTools, PromptTemplate = DefaultPrompts.Reviewer,
            Skill = DefaultPrompts.ReviewerSkill, RequiresDecision = true, TimeoutMinutes = 30,
            Description = "Claude revisa el diff y aprueba o devuelve al desarrollador.",
        };
        var prCreator = new Stage { Name = "Creador de PR", Icon = "🔀", Kind = StageKind.System,
            SystemAction = SystemAction.CreatePullRequest, Description = "Abre el pull request en GitHub." };
        var productApproval = new Stage { Name = "Aprobación Producto", Icon = "🧑‍💼", Kind = StageKind.HumanApproval,
            Description = "Producto valida el resultado. «Pedir cambios» devuelve al desarrollador." };
        var manualReview = new Stage { Name = "Revisión Código Manual", Icon = "🧐", Kind = StageKind.HumanApproval,
            Description = "Un desarrollador revisa el PR en GitHub." };
        var pendingDeploy = new Stage { Name = PendingDeployStageName, Icon = "🚀", Kind = StageKind.Manual,
            IsMergeDestination = true, Description = "La tarea entra sola aquí cuando se hace merge de su pull request." };
        var pendingTest = new Stage { Name = "Pendiente Prueba", Icon = "🧪", Kind = StageKind.Manual };
        var done = new Stage { Name = "Terminado", Icon = "🏁", Kind = StageKind.Terminal };
        var discarded = new Stage { Name = "Descartado", Icon = "🗑️", Kind = StageKind.Terminal };

        Stage[] all = [backlog, improve, approveTask, developer, reviewer, prCreator, productApproval,
            manualReview, pendingDeploy, pendingTest, done, discarded];
        for (var i = 0; i < all.Length; i++) all[i].Order = (i + 1) * 10;

        db.Stages.AddRange(all);
        await db.SaveChangesAsync(ct);

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
        await db.SaveChangesAsync(ct);
    }
}
