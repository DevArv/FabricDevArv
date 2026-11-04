using DevArv.Web.DataModels;
using DevArv.Web.DatabaseConnection;
using DevArv.Web.Helpers;
using DevArv.Web.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace DevArv.Web.Repositories;

public class ProjectsRepository(AppDbContext db)
{
    public Task<List<Project>> GetActiveAsync(CancellationToken ct) =>
        db.Projects.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.Name).ToListAsync(ct);

    public async Task<List<ProjectsRowViewModel>> GetSummaryAsync(CancellationToken ct)
    {
        var projects = await db.Projects.AsNoTracking().OrderByDescending(p => p.IsActive).ThenBy(p => p.Name).ToListAsync(ct);
        var stats = await db.Tasks.AsNoTracking()
            .GroupBy(t => t.ProjectId)
            .Select(g => new { g.Key, Open = g.Count(t => t.ArchivedAt == null), Cost = g.Sum(t => t.TotalCostUsd) })
            .ToDictionaryAsync(x => x.Key, ct);
        return projects
            .Select(p => stats.TryGetValue(p.Id, out var st)
                ? new ProjectsRowViewModel(p, st.Open, st.Cost)
                : new ProjectsRowViewModel(p, 0, 0))
            .ToList();
    }

    public async Task<ProjectEditViewModel?> GetEditAsync(int id, CancellationToken ct)
    {
        var p = await db.Projects.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return p is null
            ? null
            : new ProjectEditViewModel
            {
                Name = p.Name, Color = p.Color, Repository = p.RepoFullName, DefaultBranch = p.DefaultBranch,
                BranchPrefix = p.BranchPrefix, AgentInstructions = p.AgentInstructions, IsActive = p.IsActive,
            };
    }

    public void Validate(ProjectEditViewModel record)
    {
        if (!GitHubRepositoryParser.TryParse(record.Repository, out _, out _))
            throw new ValidationFailedException("Input.Repository",
                "Formato de repositorio inválido. Usa owner/repo o la URL de GitHub.");
    }

    /// <summary>Crea (id nulo) o actualiza un proyecto. Devuelve false si el proyecto a editar no existe.</summary>
    public async Task<bool> SaveAsync(int? id, ProjectEditViewModel record, CancellationToken ct)
    {
        Validate(record);
        GitHubRepositoryParser.TryParse(record.Repository, out var owner, out var repo);

        var project = id is null ? new Project() : await db.Projects.FindAsync([id.Value], ct);
        if (project is null) return false;

        project.Name = record.Name.Trim();
        project.Color = record.Color;
        project.RepoOwner = owner;
        project.RepoName = repo;
        project.DefaultBranch = record.DefaultBranch.Trim();
        project.BranchPrefix = record.BranchPrefix.Trim();
        project.AgentInstructions = string.IsNullOrWhiteSpace(record.AgentInstructions) ? null : record.AgentInstructions.Trim();
        project.IsActive = record.IsActive;

        if (id is null) db.Projects.Add(project);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
