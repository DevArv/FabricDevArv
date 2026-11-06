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
        // Enums legibles en la base de datos.
        ConfigurationBuilder.Properties<StageKind>().HaveConversion<string>().HaveMaxLength(32);
        ConfigurationBuilder.Properties<SystemAction>().HaveConversion<string>().HaveMaxLength(32);
        ConfigurationBuilder.Properties<WorkStatus>().HaveConversion<string>().HaveMaxLength(32);
        ConfigurationBuilder.Properties<Priority>().HaveConversion<string>().HaveMaxLength(16);
        ConfigurationBuilder.Properties<Complexity>().HaveConversion<string>().HaveMaxLength(16);
        ConfigurationBuilder.Properties<RunStatus>().HaveConversion<string>().HaveMaxLength(16);
        ConfigurationBuilder.Properties<RunDecision>().HaveConversion<string>().HaveMaxLength(16);
        ConfigurationBuilder.Properties<CommentKind>().HaveConversion<string>().HaveMaxLength(16);
        ConfigurationBuilder.Properties<AttachmentKind>().HaveConversion<string>().HaveMaxLength(16);
        ConfigurationBuilder.Properties<decimal>().HavePrecision(14, 6);
    }

    protected override void OnModelCreating(ModelBuilder Builder)
    {
        base.OnModelCreating(Builder);

        Builder.Entity<Project>(b =>
        {
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
            b.Property(s => s.Name).HasMaxLength(80).IsRequired();
            b.Property(s => s.Icon).HasMaxLength(16);
            b.Property(s => s.Model).HasMaxLength(100);
            b.Property(s => s.PermissionMode).HasMaxLength(32);
            b.Property(s => s.Skill).HasMaxLength(100);
            b.HasOne<Stage>().WithMany().HasForeignKey(s => s.SuccessStageId).OnDelete(DeleteBehavior.SetNull);
            b.HasOne<Stage>().WithMany().HasForeignKey(s => s.RejectStageId).OnDelete(DeleteBehavior.SetNull);
            b.HasIndex(s => s.Order);
            b.Ignore(s => s.RunsAutomatically);
        });

        Builder.Entity<TaskItem>(b =>
        {
            b.Property(t => t.Title).HasMaxLength(200).IsRequired();
            b.Property(t => t.BaseBranch).HasMaxLength(200);
            b.Property(t => t.WorkBranch).HasMaxLength(250);
            b.Property(t => t.Labels).HasMaxLength(500);
            b.Property(t => t.ModelOverride).HasMaxLength(100);
            b.Property(t => t.CreatedByName).HasMaxLength(256);
            b.Property(t => t.LastWorkerName).HasMaxLength(100);
            b.HasOne(t => t.Project).WithMany().HasForeignKey(t => t.ProjectId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(t => t.Stage).WithMany().HasForeignKey(t => t.StageId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(t => t.PreferredWorker).WithMany().HasForeignKey(t => t.PreferredWorkerId).OnDelete(DeleteBehavior.SetNull);
            b.HasIndex(t => new { t.ProjectId, t.ArchivedAt });
            b.HasIndex(t => t.StageId);
        });

        Builder.Entity<TaskAttachment>(b =>
        {
            b.Property(a => a.FileName).HasMaxLength(255);
            b.Property(a => a.ContentType).HasMaxLength(100);
            b.Property(a => a.StoredName).HasMaxLength(100);
            b.HasOne(a => a.Task).WithMany(t => t.Attachments).HasForeignKey(a => a.TaskId).OnDelete(DeleteBehavior.Cascade);
        });

        Builder.Entity<TaskRun>(b =>
        {
            b.Property(r => r.StageName).HasMaxLength(80);
            b.Property(r => r.Model).HasMaxLength(100);
            b.Property(r => r.SessionId).HasMaxLength(100);
            b.Property(r => r.HeadCommit).HasMaxLength(64);
            b.HasOne(r => r.Task).WithMany(t => t.Runs).HasForeignKey(r => r.TaskId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(r => r.Worker).WithMany().HasForeignKey(r => r.WorkerId).OnDelete(DeleteBehavior.SetNull);
            b.HasIndex(r => new { r.Status, r.RunnerKind, r.CreatedAt });
            b.Ignore(r => r.TotalTokens);
            b.Ignore(r => r.Duration);
        });

        Builder.Entity<TaskComment>(b =>
        {
            b.Property(c => c.Author).HasMaxLength(256);
            b.HasOne(c => c.Task).WithMany(t => t.Comments).HasForeignKey(c => c.TaskId).OnDelete(DeleteBehavior.Cascade);
        });

        Builder.Entity<TaskEvent>(b =>
        {
            b.Property(e => e.Actor).HasMaxLength(256);
            b.Property(e => e.Message).HasMaxLength(1000);
            b.HasOne(e => e.Task).WithMany(t => t.Events).HasForeignKey(e => e.TaskId).OnDelete(DeleteBehavior.Cascade);
        });

        Builder.Entity<WorkerNode>(b =>
        {
            b.Property(w => w.Name).HasMaxLength(100).IsRequired();
            b.Property(w => w.KeyHash).HasMaxLength(64).IsRequired();
            b.Property(w => w.KeyPrefix).HasMaxLength(16);
            b.Property(w => w.MachineName).HasMaxLength(100);
            b.Property(w => w.Version).HasMaxLength(40);
            b.Property(w => w.OsDescription).HasMaxLength(200);
            b.HasIndex(w => w.KeyHash).IsUnique();
            b.HasIndex(w => w.Name).IsUnique();
        });
    }
}
