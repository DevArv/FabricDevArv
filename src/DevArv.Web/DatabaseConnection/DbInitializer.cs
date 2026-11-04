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
            RequiresDecision = true, TimeoutMinutes = 30,
            Description = "Claude revisa el diff y aprueba o devuelve al desarrollador.",
        };
        var prCreator = new Stage { Name = "Creador de PR", Icon = "🔀", Kind = StageKind.System,
            SystemAction = SystemAction.CreatePullRequest, Description = "Abre el pull request en GitHub." };
        var productApproval = new Stage { Name = "Aprobación Producto", Icon = "🧑‍💼", Kind = StageKind.HumanApproval,
            Description = "Producto valida el resultado. «Pedir cambios» devuelve al desarrollador." };
        var manualReview = new Stage { Name = "Revisión Código Manual", Icon = "🧐", Kind = StageKind.HumanApproval,
            Description = "Un desarrollador revisa el PR en GitHub." };
        var pendingDeploy = new Stage { Name = "Pendiente Deploy", Icon = "🚀", Kind = StageKind.Manual };
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
