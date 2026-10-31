using DevArv.Web.Data;
using DevArv.Web.Domain;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace DevArv.Web.Pages.Projects;

public sealed class IndexModel(AppDbContext db, Services.GitHubService github) : PageModel
{
    public sealed record Row(Project Project, int OpenTasks, decimal Cost);

    public List<Row> Rows { get; private set; } = [];
    public bool GitHubConfigured => github.IsConfigured;

    public async Task OnGetAsync(CancellationToken ct)
    {
        var projects = await db.Projects.AsNoTracking().OrderByDescending(p => p.IsActive).ThenBy(p => p.Name).ToListAsync(ct);
        var stats = await db.Tasks.AsNoTracking()
            .GroupBy(t => t.ProjectId)
            .Select(g => new { g.Key, Open = g.Count(t => t.ArchivedAt == null), Cost = g.Sum(t => t.TotalCostUsd) })
            .ToDictionaryAsync(x => x.Key, ct);
        Rows = projects.Select(p => new Row(p,
            stats.TryGetValue(p.Id, out var st) ? st.Open : 0,
            stats.TryGetValue(p.Id, out var st2) ? st2.Cost : 0)).ToList();
    }
}
