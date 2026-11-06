using DevArv.Web.Repositories;
using DevArv.Web.Services;
using DevArv.Web.ViewModels;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DevArv.Web.Pages.Projects;

public sealed class IndexModel(ProjectsRepository ProjectsRepository, GitHubService GitHubService) : PageModel
{
    public List<ProjectsRowViewModel> Rows { get; private set; } = [];
    public bool GitHubConfigured => GitHubService.IsConfigured;

    public async Task OnGetAsync(CancellationToken CT)
    {
        Rows = await ProjectsRepository.GetSummaryAsync(CT);
    }
}
