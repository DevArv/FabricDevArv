using DevArv.Web.DataModels;
using DevArv.Web.Enums.Runs;
using DevArv.Web.Enums.Tasks;
using DevArv.Web.Enums.Workflow;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DevArv.Web.DatabaseConnection;

public class AppDbContext(DbContextOptions<AppDbContext> Options) : IdentityDbContext<IdentityUser>(Options)
{
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Stage> Stages => Set<Stage>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<TaskAttachment> TaskAttachments => Set<TaskAttachment>();
    public DbSet<TaskRun> TaskRuns => Set<TaskRun>();
    public DbSet<TaskComment> TaskComments => Set<TaskComment>();
    public DbSet<TaskEvent> TaskEvents => Set<TaskEvent>();
    public DbSet<WorkerNode> Workers => Set<WorkerNode>();

    protected override void ConfigureConventions(ModelConfigurationBuilder ConfigurationBuilder)
    {
        // Enums legibles en la base de datos (texto en PascalCase; ver EnumNameStringConverter).
        ConfigurationBuilder.Properties<StageKind>().HaveConversion<EnumNameStringConverter<StageKind>>().HaveMaxLength(32);
        ConfigurationBuilder.Properties<SystemAction>().HaveConversion<EnumNameStringConverter<SystemAction>>().HaveMaxLength(32);
        ConfigurationBuilder.Properties<WorkStatus>().HaveConversion<EnumNameStringConverter<WorkStatus>>().HaveMaxLength(32);
        ConfigurationBuilder.Properties<Priority>().HaveConversion<EnumNameStringConverter<Priority>>().HaveMaxLength(16);
        ConfigurationBuilder.Properties<Complexity>().HaveConversion<EnumNameStringConverter<Complexity>>().HaveMaxLength(16);
        ConfigurationBuilder.Properties<RunStatus>().HaveConversion<EnumNameStringConverter<RunStatus>>().HaveMaxLength(16);
        ConfigurationBuilder.Properties<RunDecision>().HaveConversion<EnumNameStringConverter<RunDecision>>().HaveMaxLength(16);
        ConfigurationBuilder.Properties<CommentKind>().HaveConversion<EnumNameStringConverter<CommentKind>>().HaveMaxLength(16);
        ConfigurationBuilder.Properties<AttachmentKind>().HaveConversion<EnumNameStringConverter<AttachmentKind>>().HaveMaxLength(16);
        ConfigurationBuilder.Properties<decimal>().HavePrecision(14, 6);
    }

    protected override void OnModelCreating(ModelBuilder Builder)
    {
        base.OnModelCreating(Builder);

        // Tablas de Identity con el prefijo de su módulo.
        Builder.Entity<IdentityUser>().ToTable(TableNames.IDENTITY_USERS);
        Builder.Entity<IdentityRole>().ToTable(TableNames.IDENTITY_ROLES);
        Builder.Entity<IdentityUserRole<string>>().ToTable(TableNames.IDENTITY_USER_ROLES);
        Builder.Entity<IdentityUserClaim<string>>().ToTable(TableNames.IDENTITY_USER_CLAIMS);
        Builder.Entity<IdentityUserLogin<string>>().ToTable(TableNames.IDENTITY_USER_LOGINS);
        Builder.Entity<IdentityUserToken<string>>().ToTable(TableNames.IDENTITY_USER_TOKENS);
        Builder.Entity<IdentityRoleClaim<string>>().ToTable(TableNames.IDENTITY_ROLE_CLAIMS);

        Builder.Entity<Project>(b =>
        {
            b.ToTable(TableNames.PROJECTS);
            b.Property(p => p.Name).HasMaxLength(120).IsRequired();
            b.Property(p => p.Color).HasMaxLength(16);
            b.Property(p => p.RepoOwner).HasMaxLength(100).IsRequired();
            b.Property(p => p.RepoName).HasMaxLength(100).IsRequired();
            b.Property(p => p.DefaultBranch).HasMaxLength(200);
            b.Property(p => p.BranchPrefix).HasMaxLength(60);
            b.Ignore(p => p.RepoFullName);
        });

        Builder.Entity<Stage>(b =>
        {
            b.ToTable(TableNames.STAGES);
            b.Property(s => s.Name).HasMaxLength(80).IsRequired();
            b.Property(s => s.Icon).HasMaxLength(16);
            b.Property(s => s.Model).HasMaxLength(100);
            b.Property(s => s.PermissionMode).HasMaxLength(32);
            b.Property(s => s.Skill).HasMaxLength(100);
            b.HasOne<Stage>().WithMany().HasForeignKey(s => s.SuccessStageID).OnDelete(DeleteBehavior.SetNull);
            b.HasOne<Stage>().WithMany().HasForeignKey(s => s.RejectStageID).OnDelete(DeleteBehavior.SetNull);
            b.HasIndex(s => s.Order);
            b.Ignore(s => s.RunsAutomatically);
        });

        Builder.Entity<TaskItem>(b =>
        {
            b.ToTable(TableNames.TASKS);
            b.Property(t => t.Title).HasMaxLength(200).IsRequired();
            b.Property(t => t.BaseBranch).HasMaxLength(200);
            b.Property(t => t.WorkBranch).HasMaxLength(250);
            b.Property(t => t.Labels).HasMaxLength(500);
            b.Property(t => t.ModelOverride).HasMaxLength(100);
            b.Property(t => t.CreatedByName).HasMaxLength(256);
            b.Property(t => t.LastWorkerName).HasMaxLength(100);
            b.HasOne(t => t.Project).WithMany().HasForeignKey(t => t.ProjectID).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(t => t.Stage).WithMany().HasForeignKey(t => t.StageID).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(t => t.PreferredWorker).WithMany().HasForeignKey(t => t.PreferredWorkerID).OnDelete(DeleteBehavior.SetNull);
            b.HasIndex(t => new { t.ProjectID, t.ArchivedAt });
            b.HasIndex(t => t.StageID);
        });

        Builder.Entity<TaskAttachment>(b =>
        {
            b.ToTable(TableNames.TASK_ATTACHMENTS);
            b.Property(a => a.FileName).HasMaxLength(255);
            b.Property(a => a.ContentType).HasMaxLength(100);
            b.Property(a => a.StoredName).HasMaxLength(100);
            b.HasOne(a => a.Task).WithMany(t => t.Attachments).HasForeignKey(a => a.TaskID).OnDelete(DeleteBehavior.Cascade);
        });

        Builder.Entity<TaskRun>(b =>
        {
            b.ToTable(TableNames.TASK_RUNS);
            b.Property(r => r.StageName).HasMaxLength(80);
            b.Property(r => r.Model).HasMaxLength(100);
            b.Property(r => r.SessionID).HasMaxLength(100);
            b.Property(r => r.HeadCommit).HasMaxLength(64);
            b.HasOne(r => r.Task).WithMany(t => t.Runs).HasForeignKey(r => r.TaskID).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(r => r.Worker).WithMany().HasForeignKey(r => r.WorkerID).OnDelete(DeleteBehavior.SetNull);
            b.HasIndex(r => new { r.Status, r.RunnerKind, r.CreatedAt });
            b.Ignore(r => r.TotalTokens);
            b.Ignore(r => r.Duration);
        });

        Builder.Entity<TaskComment>(b =>
        {
            b.ToTable(TableNames.TASK_COMMENTS);
            b.Property(c => c.Author).HasMaxLength(256);
            b.HasOne(c => c.Task).WithMany(t => t.Comments).HasForeignKey(c => c.TaskID).OnDelete(DeleteBehavior.Cascade);
        });

        Builder.Entity<TaskEvent>(b =>
        {
            b.ToTable(TableNames.TASK_EVENTS);
            b.Property(e => e.Actor).HasMaxLength(256);
            b.Property(e => e.Message).HasMaxLength(1000);
            b.HasOne(e => e.Task).WithMany(t => t.Events).HasForeignKey(e => e.TaskID).OnDelete(DeleteBehavior.Cascade);
        });

        Builder.Entity<WorkerNode>(b =>
        {
            b.ToTable(TableNames.WORKERS);
            b.Property(w => w.Name).HasMaxLength(100).IsRequired();
            b.Property(w => w.KeyHash).HasMaxLength(64).IsRequired();
            b.Property(w => w.KeyPrefix).HasMaxLength(16);
            b.Property(w => w.MachineName).HasMaxLength(100);
            b.Property(w => w.Version).HasMaxLength(40);
            b.Property(w => w.OsDescription).HasMaxLength(200);
            b.HasIndex(w => w.KeyHash).IsUnique();
            b.HasIndex(w => w.Name).IsUnique();
        });

        // En el código la palabra «ID» va en mayúsculas (TaskID); las columnas conservan su nombre histórico (TaskId),
        // así que las bases ya creadas no necesitan renombrar columnas.
        foreach (var entityType in Builder.Model.GetEntityTypes().Where(e => e.ClrType.Namespace == typeof(Project).Namespace))
        {
            foreach (var property in entityType.GetProperties())
                property.SetColumnName(ToColumnName(property.Name));
        }
    }

    /// <summary>Nombre de la columna de una propiedad: «TaskID» → «TaskId», «ID» → «Id».</summary>
    internal static string ToColumnName(string PropertyName) =>
        PropertyName.EndsWith("ID", StringComparison.Ordinal) ? PropertyName[..^2] + "Id" : PropertyName;
}
