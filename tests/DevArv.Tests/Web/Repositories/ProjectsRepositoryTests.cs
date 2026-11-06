using System.Net;
using DevArv.Tests.TestSupport;
using DevArv.Web.DataModels;
using DevArv.Web.DatabaseConnection;
using DevArv.Web.Models;
using DevArv.Web.Repositories;
using DevArv.Web.Services;
using DevArv.Web.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DevArv.Tests.Web.Repositories;

public sealed class ProjectsRepositoryTests
{
    private readonly string _DatabaseName = Guid.NewGuid().ToString("N");

    private AppDbContext CreateDbContext() => TestDatabase.CreateContext(_DatabaseName);

    private static GitHubService CreateGitHub(Func<CapturedRequest, HttpResponseMessage>? Respond = null, string Token = "token") =>
        new(new HttpClient(new StubHttpHandler(Respond ?? (_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)))),
            Options.Create(new GitHubOptions { Token = Token }));

    private ProjectsRepository CreateRepository(AppDbContext Db, GitHubService? GitHub = null) =>
        new(Db, GitHub ?? CreateGitHub());

    private static ProjectEditViewModel ValidRecord() => new()
    {
        Name = "Fabric",
        Repository = "acme/fabric",
        DefaultBranch = "main",
        BranchPrefix = "devarv/",
    };

    private static HttpResponseMessage Json(HttpStatusCode Status, string Body) => StubHttpHandler.Json(Status, Body);

    // ------------------------------------------------------------------ lecturas

    [Fact]
    public async Task GetActiveAsync_ReturnsOnlyActiveProjectsOrderedByName()
    {
        await using (var db = CreateDbContext())
        {
            db.Projects.AddRange(
                new Project { Name = "Zeta", RepoOwner = "o", RepoName = "z" },
                new Project { Name = "Alfa", RepoOwner = "o", RepoName = "a" },
                new Project { Name = "Inactivo", RepoOwner = "o", RepoName = "i", IsActive = false });
            await db.SaveChangesAsync();
        }

        await using var check = CreateDbContext();
        var result = await CreateRepository(check).GetActiveAsync(CancellationToken.None);

        Assert.Equal(["Alfa", "Zeta"], result.Select(p => p.Name));
    }

    [Fact]
    public async Task GetSummaryAsync_CountsOpenTasksAndSumsCost_AndShowsEmptyProjectsWithZero()
    {
        await using (var db = CreateDbContext())
        {
            db.Stages.Add(new Stage { ID = 1, Name = "S" });
            db.Projects.AddRange(
                new Project { ID = 1, Name = "Con tareas", RepoOwner = "o", RepoName = "a" },
                new Project { ID = 2, Name = "Vacío", RepoOwner = "o", RepoName = "b" });
            db.Tasks.AddRange(
                new TaskItem { Title = "1", ProjectID = 1, StageID = 1, TotalCostUsd = 1.5m },
                new TaskItem { Title = "2", ProjectID = 1, StageID = 1, TotalCostUsd = 2m },
                new TaskItem { Title = "archivada", ProjectID = 1, StageID = 1, TotalCostUsd = 3m, ArchivedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        await using var check = CreateDbContext();
        var rows = await CreateRepository(check).GetSummaryAsync(CancellationToken.None);

        var withTasks = rows.Single(r => r.Project.Name == "Con tareas");
        Assert.Equal(2, withTasks.OpenTasks);
        Assert.Equal(6.5m, withTasks.Cost);
        var empty = rows.Single(r => r.Project.Name == "Vacío");
        Assert.Equal(0, empty.OpenTasks);
        Assert.Equal(0m, empty.Cost);
    }

    [Fact]
    public async Task GetEditAsync_ReturnsTheRepositoryAsOwnerSlashRepo()
    {
        int id;
        await using (var db = CreateDbContext())
        {
            var project = new Project { Name = "Fabric", RepoOwner = "acme", RepoName = "fabric", AgentInstructions = "Usa C#." };
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            id = project.ID;
        }

        await using var check = CreateDbContext();
        var record = await CreateRepository(check).GetEditAsync(
            ID: id,
            CT: CancellationToken.None);

        Assert.NotNull(record);
        Assert.Equal("acme/fabric", record.Repository);
        Assert.Equal("Usa C#.", record.AgentInstructions);
    }

    [Fact]
    public async Task GetEditAsync_UnknownProject_ReturnsNull()
    {
        await using var db = CreateDbContext();

        var record = await CreateRepository(db).GetEditAsync(
            ID: 999,
            CT: CancellationToken.None);

        Assert.Null(record);
    }

    // ------------------------------------------------------------------ validar y guardar

    [Theory]
    [InlineData("")]
    [InlineData("sin-barra")]
    [InlineData("a/b/c/d")]
    public async Task Validate_InvalidRepository_Throws(string Repository)
    {
        await using var db = CreateDbContext();
        var record = ValidRecord();
        record.Repository = Repository;

        var ex = Assert.Throws<ValidationFailedException>(() => CreateRepository(db).Validate(record));

        Assert.Equal("Input.Repository", ex.Errors.Single().Field);
    }

    [Theory]
    [InlineData("acme/fabric")]
    [InlineData("https://github.com/acme/fabric")]
    [InlineData("git@github.com:acme/fabric.git")]
    public async Task Validate_AcceptedFormats_DoNotThrow(string Repository)
    {
        await using var db = CreateDbContext();
        var record = ValidRecord();
        record.Repository = Repository;

        CreateRepository(db).Validate(record);
    }

    [Fact]
    public async Task SaveAsync_NewProject_SplitsTheRepositoryAndTrimsTheFields()
    {
        await using var db = CreateDbContext();
        var record = ValidRecord();
        record.Name = "  Fabric  ";
        record.Repository = "https://github.com/acme/fabric.git";
        record.DefaultBranch = " main ";
        record.BranchPrefix = " fd/ ";
        record.AgentInstructions = "   ";

        var saved = await CreateRepository(db).SaveAsync(
            ID: null,
            Record: record,
            CT: CancellationToken.None);

        Assert.True(saved);
        await using var check = CreateDbContext();
        var project = await check.Projects.SingleAsync();
        Assert.Equal("Fabric", project.Name);
        Assert.Equal("acme", project.RepoOwner);
        Assert.Equal("fabric", project.RepoName);
        Assert.Equal("main", project.DefaultBranch);
        Assert.Equal("fd/", project.BranchPrefix);
        Assert.Null(project.AgentInstructions);
    }

    [Fact]
    public async Task SaveAsync_ExistingProject_UpdatesIt()
    {
        int id;
        await using (var db = CreateDbContext())
        {
            var project = new Project { Name = "Vieja", RepoOwner = "o", RepoName = "r" };
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            id = project.ID;
        }
        await using var update = CreateDbContext();
        var record = ValidRecord();
        record.IsActive = false;

        var saved = await CreateRepository(update).SaveAsync(
            ID: id,
            Record: record,
            CT: CancellationToken.None);

        Assert.True(saved);
        await using var check = CreateDbContext();
        var project2 = await check.Projects.SingleAsync();
        Assert.Equal("Fabric", project2.Name);
        Assert.False(project2.IsActive);
        Assert.Equal(1, await check.Projects.CountAsync());
    }

    [Fact]
    public async Task SaveAsync_UnknownProject_ReturnsFalseAndCreatesNothing()
    {
        await using var db = CreateDbContext();

        var saved = await CreateRepository(db).SaveAsync(
            ID: 999,
            Record: ValidRecord(),
            CT: CancellationToken.None);

        Assert.False(saved);
        await using var check = CreateDbContext();
        Assert.Equal(0, await check.Projects.CountAsync());
    }

    [Fact]
    public async Task SaveAsync_InvalidRepository_ThrowsAndCreatesNothing()
    {
        await using var db = CreateDbContext();
        var record = ValidRecord();
        record.Repository = "no-valido";

        await Assert.ThrowsAsync<ValidationFailedException>(() => CreateRepository(db).SaveAsync(
            ID: null,
            Record: record,
            CT: CancellationToken.None));

        await using var check = CreateDbContext();
        Assert.Equal(0, await check.Projects.CountAsync());
    }

    // ------------------------------------------------------------------ probar la conexión

    [Fact]
    public async Task TestConnectionAsync_InvalidFormat_ReturnsAnErrorWithoutCallingGitHub()
    {
        await using var db = CreateDbContext();
        var calls = 0;
        var github = CreateGitHub(_ => { calls++; return Json(HttpStatusCode.OK, "{}"); });
        var record = ValidRecord();
        record.Repository = "no-valido";

        var result = await CreateRepository(db, github).TestConnectionAsync(
            Record: record,
            CT: CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task TestConnectionAsync_Success_NormalizesTheRepositoryAndKeepsAnExistingBranch()
    {
        await using var db = CreateDbContext();
        var github = CreateGitHub(request => request.PathAndQuery.Split('?')[0] switch
        {
            "/repos/acme/fabric" => Json(HttpStatusCode.OK,
                """{"full_name":"acme/Fabric","default_branch":"main","private":true,"html_url":"https://github.com/acme/Fabric"}"""),
            "/repos/acme/fabric/branches" => Json(HttpStatusCode.OK, """[{"name":"main"},{"name":"desarrollo"}]"""),
            _ => Json(HttpStatusCode.NotFound, """{"message":"Not Found"}"""),
        });
        var record = ValidRecord();
        record.DefaultBranch = "desarrollo";

        var result = await CreateRepository(db, github).TestConnectionAsync(
            Record: record,
            CT: CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains("privado", result.Message);
        Assert.Equal("acme/Fabric", record.Repository);
        Assert.Equal("desarrollo", record.DefaultBranch);
    }

    [Fact]
    public async Task TestConnectionAsync_UnknownBranch_FallsBackToTheRepositoryDefault()
    {
        await using var db = CreateDbContext();
        var github = CreateGitHub(request => request.PathAndQuery.Contains("/branches")
            ? Json(HttpStatusCode.OK, """[{"name":"master"}]""")
            : Json(HttpStatusCode.OK, """{"full_name":"acme/fabric","default_branch":"master","private":false,"html_url":"x"}"""));
        var record = ValidRecord();
        record.DefaultBranch = "no-existe";

        var result = await CreateRepository(db, github).TestConnectionAsync(
            Record: record,
            CT: CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains("público", result.Message);
        Assert.Equal("master", record.DefaultBranch);
    }

    [Fact]
    public async Task TestConnectionAsync_GitHubFails_ReturnsTheErrorAndKeepsTheRecord()
    {
        await using var db = CreateDbContext();
        var github = CreateGitHub(_ => Json(HttpStatusCode.NotFound, """{"message":"Not Found"}"""));
        var record = ValidRecord();

        var result = await CreateRepository(db, github).TestConnectionAsync(
            Record: record,
            CT: CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("404", result.Message);
        Assert.Equal("acme/fabric", record.Repository);
    }

    [Fact]
    public async Task TestConnectionAsync_WithoutToken_ExplainsWhatIsMissing()
    {
        await using var db = CreateDbContext();
        var github = CreateGitHub(Token: "");

        var result = await CreateRepository(db, github).TestConnectionAsync(
            Record: ValidRecord(),
            CT: CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("GitHub:Token", result.Message);
    }
}
