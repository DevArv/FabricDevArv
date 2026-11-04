using DevArv.Web.Repositories;
using DevArv.Web.Services;
using DevArv.Web.ViewModels;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DevArv.Web.Pages.Projects;

public sealed class IndexModel(ProjectsRepository projects, GitHubService github) : PageModel
{
    public List<ProjectsRowViewModel> Rows { get; private set; } = [];
    public bool GitHubConfigured => github.IsConfigured;

    public async Task OnGetAsync(CancellationToken ct)
    {
        Rows = await projects.GetSummaryAsync(ct);
    }
}
