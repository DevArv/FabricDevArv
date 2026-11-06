using DevArv.Web.DataModels;
using DevArv.Web.DatabaseConnection;
using DevArv.Web.Helpers;
using DevArv.Web.Services;
using DevArv.Web.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace DevArv.Web.Repositories;

public class ProjectsRepository(AppDbContext Db, GitHubService GitHub)
{
    public Task<List<Project>> GetActiveAsync(CancellationToken CT) =>
        Db.Projects.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.Name).ToListAsync(CT);

    public async Task<List<ProjectsRowViewModel>> GetSummaryAsync(CancellationToken CT)
    {
        var projects = await Db.Projects.AsNoTracking().OrderByDescending(p => p.IsActive).ThenBy(p => p.Name).ToListAsync(CT);
        var stats = await Db.Tasks.AsNoTracking()
            .GroupBy(t => t.ProjectID)
            .Select(g => new { g.Key, Open = g.Count(t => t.ArchivedAt == null), Cost = g.Sum(t => t.TotalCostUsd) })
            .ToDictionaryAsync(x => x.Key, CT);
        return projects
            .Select(p => stats.TryGetValue(p.ID, out var stat)
                ? new ProjectsRowViewModel(p, stat.Open, stat.Cost)
                : new ProjectsRowViewModel(p, 0, 0))
            .ToList();
    }

    public async Task<ProjectEditViewModel?> GetEditAsync(int ID, CancellationToken CT)
    {
        var project = await Db.Projects.AsNoTracking().FirstOrDefaultAsync(x => x.ID == ID, CT);
        return project is null
            ? null
            : new ProjectEditViewModel
            {
                Name = project.Name, Color = project.Color, Repository = project.RepoFullName, DefaultBranch = project.DefaultBranch,
                BranchPrefix = project.BranchPrefix, AgentInstructions = project.AgentInstructions, IsActive = project.IsActive,
            };
    }

    /// <summary>
    /// Prueba el acceso al repositorio de GitHub. Si la conexión es correcta normaliza <paramref name="Record"/>:
    /// el repositorio queda como «owner/repo» y, si la rama por defecto no existe, se usa la del repositorio.
    /// </summary>
    public async Task<ProjectConnectionTestViewModel> TestConnectionAsync(ProjectEditViewModel Record, CancellationToken CT)
    {
        if (!GitHubRepositoryParser.TryParse(
                Input: Record.Repository,
                Owner: out var owner,
                Repo: out var repo))
            return new ProjectConnectionTestViewModel(
                "Formato de repositorio inválido. Usa owner/repo o https://github.com/owner/repo.", true);
        try
        {
            var details = await GitHub.GetRepositoryAsync(
                Owner: owner,
                Repo: repo,
                CT: CT);
            var branches = await GitHub.ListBranchesAsync(
                Owner: owner,
                Repo: repo,
                CT: CT);
            Record.Repository = details.FullName;
            if (string.IsNullOrWhiteSpace(Record.DefaultBranch) || !branches.Contains(Record.DefaultBranch))
                Record.DefaultBranch = details.DefaultBranch;
            return new ProjectConnectionTestViewModel(
                $"✓ Conexión correcta con {details.FullName} ({(details.IsPrivate ? "privado" : "público")}). " +
                $"Ramas: {string.Join(", ", branches.Take(15))}{(branches.Count > 15 ? "…" : "")}", false);
        }
        catch (GitHubException ex)
        {
            return new ProjectConnectionTestViewModel(ex.Message, true);
        }
    }

    public void Validate(ProjectEditViewModel Record)
    {
        if (!GitHubRepositoryParser.TryParse(
                Input: Record.Repository,
                Owner: out _,
                Repo: out _))
            throw new ValidationFailedException("Input.Repository",
                "Formato de repositorio inválido. Usa owner/repo o la URL de GitHub.");
    }

    /// <summary>Crea (ID nulo) o actualiza un proyecto. Devuelve false si el proyecto a editar no existe.</summary>
    public async Task<bool> SaveAsync(int? ID, ProjectEditViewModel Record, CancellationToken CT)
    {
        Validate(Record);
        GitHubRepositoryParser.TryParse(
            Input: Record.Repository,
            Owner: out var owner,
            Repo: out var repo);

        var project = ID is null ? new Project() : await Db.Projects.FindAsync([ID.Value], CT);
        if (project is null) return false;

        project.Name = Record.Name.Trim();
        project.Color = Record.Color;
        project.RepoOwner = owner;
        project.RepoName = repo;
        project.DefaultBranch = Record.DefaultBranch.Trim();
        project.BranchPrefix = Record.BranchPrefix.Trim();
        project.AgentInstructions = string.IsNullOrWhiteSpace(Record.AgentInstructions) ? null : Record.AgentInstructions.Trim();
        project.IsActive = Record.IsActive;

        if (ID is null) Db.Projects.Add(project);
        await Db.SaveChangesAsync(CT);
        return true;
    }
}
