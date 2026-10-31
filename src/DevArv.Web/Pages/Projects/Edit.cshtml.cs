using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using DevArv.Web.Data;
using DevArv.Web.Domain;
using DevArv.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace DevArv.Web.Pages.Projects;

public sealed partial class EditModel(AppDbContext db, GitHubService github) : PageModel
{
    public sealed class InputModel
    {
        [Required(ErrorMessage = "El nombre es obligatorio."), MaxLength(120)]
        public string Name { get; set; } = "";

        [RegularExpression("^#[0-9a-fA-F]{6}$", ErrorMessage = "Color inválido.")]
        public string Color { get; set; } = "#3b82f6";

        /// <summary>Acepta "owner/repo" o la URL completa de GitHub.</summary>
        [Required(ErrorMessage = "Indica el repositorio (owner/repo o URL).")]
        public string Repository { get; set; } = "";

        [Required, MaxLength(200)]
        [RegularExpression(@"^[A-Za-z0-9._/\-]+$", ErrorMessage = "Nombre de rama inválido.")]
        public string DefaultBranch { get; set; } = "main";

        [Required, MaxLength(60)]
        [RegularExpression(@"^[A-Za-z0-9._/\-]*$", ErrorMessage = "Prefijo inválido.")]
        public string BranchPrefix { get; set; } = "devarv/";

        [MaxLength(20_000)]
        public string? AgentInstructions { get; set; }

        public bool IsActive { get; set; } = true;
    }

    [BindProperty(SupportsGet = true)] public int? Id { get; set; }
    [BindProperty] public InputModel Input { get; set; } = new();
    public string? Message { get; private set; }
    public bool MessageIsError { get; private set; }
    public bool GitHubConfigured => github.IsConfigured;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (Id is null) return Page();
        var p = await db.Projects.FindAsync([Id.Value], ct);
        if (p is null) return NotFound();
        Input = new InputModel
        {
            Name = p.Name, Color = p.Color, Repository = p.RepoFullName, DefaultBranch = p.DefaultBranch,
            BranchPrefix = p.BranchPrefix, AgentInstructions = p.AgentInstructions, IsActive = p.IsActive,
        };
        return Page();
    }

    public async Task<IActionResult> OnPostTestAsync(CancellationToken ct)
    {
        ModelState.Clear();
        if (!TryParseRepository(Input.Repository, out var owner, out var repo))
        {
            SetMessage("Formato de repositorio inválido. Usa owner/repo o https://github.com/owner/repo.", true);
            return Page();
        }
        try
        {
            var details = await github.GetRepositoryAsync(owner, repo, ct);
            var branches = await github.ListBranchesAsync(owner, repo, ct);
            Input.Repository = details.FullName;
            if (string.IsNullOrWhiteSpace(Input.DefaultBranch) || !branches.Contains(Input.DefaultBranch))
                Input.DefaultBranch = details.DefaultBranch;
            SetMessage($"✓ Conexión correcta con {details.FullName} ({(details.IsPrivate ? "privado" : "público")}). " +
                       $"Ramas: {string.Join(", ", branches.Take(15))}{(branches.Count > 15 ? "…" : "")}", false);
        }
        catch (GitHubException ex)
        {
            SetMessage(ex.Message, true);
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!TryParseRepository(Input.Repository, out var owner, out var repo))
            ModelState.AddModelError("Input.Repository", "Formato de repositorio inválido. Usa owner/repo o la URL de GitHub.");
        if (!ModelState.IsValid) return Page();

        var project = Id is null ? new Project() : await db.Projects.FindAsync([Id.Value], ct);
        if (project is null) return NotFound();

        project.Name = Input.Name.Trim();
        project.Color = Input.Color;
        project.RepoOwner = owner;
        project.RepoName = repo;
        project.DefaultBranch = Input.DefaultBranch.Trim();
        project.BranchPrefix = Input.BranchPrefix.Trim();
        project.AgentInstructions = string.IsNullOrWhiteSpace(Input.AgentInstructions) ? null : Input.AgentInstructions.Trim();
        project.IsActive = Input.IsActive;

        if (Id is null) db.Projects.Add(project);
        await db.SaveChangesAsync(ct);
        return RedirectToPage("/Projects/Index");
    }

    private void SetMessage(string message, bool isError)
    {
        Message = message;
        MessageIsError = isError;
    }

    internal static bool TryParseRepository(string? input, out string owner, out string repo)
    {
        owner = repo = "";
        if (string.IsNullOrWhiteSpace(input)) return false;
        var m = RepoPattern().Match(input.Trim());
        if (!m.Success) return false;
        owner = m.Groups["owner"].Value;
        repo = m.Groups["repo"].Value;
        if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) repo = repo[..^4];
        return owner.Length > 0 && repo.Length > 0;
    }

    [GeneratedRegex(@"^(?:(?:https?://)?(?:www\.)?github\.com/|git@github\.com:)?(?<owner>[A-Za-z0-9](?:[A-Za-z0-9-]{0,38}))/(?<repo>[A-Za-z0-9._-]{1,100})/?$")]
    private static partial Regex RepoPattern();
}
