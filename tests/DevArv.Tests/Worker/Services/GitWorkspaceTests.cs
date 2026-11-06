using System.Diagnostics;
using DevArv.Contracts.Models;
using DevArv.Worker.Models;
using DevArv.Worker.Services;
using Microsoft.Extensions.Options;

namespace DevArv.Tests.Worker.Services;

public sealed class GitWorkspaceTests : IDisposable
{
    private readonly string _Root = Path.Combine(Path.GetTempPath(), $"devarv-git-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_Root)) GitWorkspace.DeleteDirectory(_Root);
    }

    [Fact]
    public void ParsesNameStatusIncludingRenames()
    {
        var files = GitWorkspace.ParseNameStatus("M\tsrc/a.cs\nA\tsrc/b.cs\nR087\told/c.cs\tnew/c.cs\nD\tx.txt\n");
        Assert.Equal(
            new[] { new ChangedFile("M", "src/a.cs"), new ChangedFile("A", "src/b.cs"), new ChangedFile("R", "new/c.cs"), new ChangedFile("D", "x.txt") },
            files);
    }

    [Fact]
    public void LeftoverConflictMarkers_OnlyMarkersOfConflictedFiles()
    {
        const string output = """
            src/a.cs:10: leftover conflict marker
            src/a.cs:12: leftover conflict marker
            src/a.cs:20: trailing whitespace.
            +    var x = 1;
            docs/readme.md:3: leftover conflict marker
            """;

        Assert.Equal(["src/a.cs:10", "src/a.cs:12"], GitWorkspace.LeftoverConflictMarkers(output, ["src/a.cs"]));
        Assert.Empty(GitWorkspace.LeftoverConflictMarkers("", ["src/a.cs"]));
    }

    [Fact]
    public async Task MergeBaseAsync_WithConflicts_LeavesThemAndBlocksCommitWhileMarkersRemain()
    {
        var (git, job, ws) = await SetUpAsync(baseChange: ("a.txt", "uno\nmain\n"), workChange: ("a.txt", "uno\ntarea\n"));

        var conflicted = await git.MergeBaseAsync(job, ws, _ => { }, CancellationToken.None);

        Assert.Equal(["a.txt"], conflicted);
        Assert.Contains("<<<<<<<", await File.ReadAllTextAsync(Path.Combine(ws.WorktreePath, "a.txt")));
        var ex = await Assert.ThrowsAsync<GitException>(() =>
            git.CommitAndPushAsync(job, ws, _ => { }, CancellationToken.None, conflicted));
        Assert.Contains("marcadores de conflicto", ex.Message);
        Assert.Contains("a.txt:", ex.Message);
        Assert.Equal(ws.StartCommit, await RunGitAsync(ws.WorktreePath, "rev-parse", "HEAD"));
    }

    [Fact]
    public async Task MergeBaseAsync_WithoutConflicts_CreatesTheMergeCommit()
    {
        var (git, job, ws) = await SetUpAsync(baseChange: ("b.txt", "nuevo en main\n"), workChange: ("a.txt", "uno\ntarea\n"));

        var conflicted = await git.MergeBaseAsync(job, ws, _ => { }, CancellationToken.None);

        Assert.Empty(conflicted);
        var parents = (await RunGitAsync(ws.WorktreePath, "rev-list", "--parents", "-n", "1", "HEAD")).Split(' ');
        Assert.Equal(3, parents.Length); // el commit y sus dos padres
        Assert.True(File.Exists(Path.Combine(ws.WorktreePath, "b.txt")));
    }

    /// <summary>
    /// Repositorio «remoto» con <c>main</c> y la rama de trabajo <c>fd_/1-tarea</c> divergentes, y un clon local en la
    /// rama de trabajo (como lo deja <see cref="GitWorkspace.PrepareAsync"/>).
    /// </summary>
    private async Task<(GitWorkspace Git, AgentJob Job, PreparedWorkspace Workspace)> SetUpAsync(
        (string File, string Content) baseChange, (string File, string Content) workChange)
    {
        const string workBranch = "fd_/1-tarea";
        var remote = Path.Combine(_Root, "remote");
        var local = Path.Combine(_Root, "local");
        Directory.CreateDirectory(remote);

        await RunGitAsync(remote, "init", "-q", "-b", "main");
        await ConfigureAuthorAsync(remote);
        await File.WriteAllTextAsync(Path.Combine(remote, "a.txt"), "uno\ndos\n");
        await RunGitAsync(remote, "add", "-A");
        await RunGitAsync(remote, "commit", "-q", "-m", "inicio");
        await RunGitAsync(remote, "checkout", "-q", "-b", workBranch);
        await CommitAsync(remote, workChange, "cambio de la tarea");
        await RunGitAsync(remote, "checkout", "-q", "main");
        await CommitAsync(remote, baseChange, "cambio en main");

        await RunGitAsync(_Root, "clone", "-q", remote, local);
        await ConfigureAuthorAsync(local);
        await RunGitAsync(local, "checkout", "-q", "-B", workBranch, $"origin/{workBranch}");
        var start = await RunGitAsync(local, "rev-parse", "HEAD");

        var git = new GitWorkspace(Options.Create(new WorkerOptions()));
        var job = new AgentJob(1, 1, "Tarea", "Desarrollador", new RepoInfo("o", "r", "https://github.com/o/r.git"),
            "main", workBranch, "prompt", null, null, "acceptEdits", [], [], null, [], PushChanges: true, 60,
            "DevArv #1: Tarea", [], MergeBaseBranch: true);
        return (git, job, new PreparedWorkspace(local, Path.Combine(_Root, "attachments"), start, RemoteBranchExisted: true));
    }

    private static async Task CommitAsync(string repo, (string File, string Content) change, string message)
    {
        await File.WriteAllTextAsync(Path.Combine(repo, change.File), change.Content);
        await RunGitAsync(repo, "add", "-A");
        await RunGitAsync(repo, "commit", "-q", "-m", message);
    }

    private static async Task ConfigureAuthorAsync(string repo)
    {
        await RunGitAsync(repo, "config", "user.name", "Pruebas");
        await RunGitAsync(repo, "config", "user.email", "pruebas@example.com");
        await RunGitAsync(repo, "config", "commit.gpgsign", "false");
    }

    private static async Task<string> RunGitAsync(string cwd, params string[] args)
    {
        var info = new ProcessStartInfo("git") { WorkingDirectory = cwd, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', args)} falló: {await stderr}");
        return (await stdout).Trim();
    }
}
