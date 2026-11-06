using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using DevArv.Contracts.Models;
using Microsoft.Extensions.Options;
using DevArv.Worker.Models;
using DevArv.Worker.Helpers;

namespace DevArv.Worker.Services;

/// <summary>
/// Mantiene un clon por repositorio y un worktree por tarea:
///   {root}/repos/{owner}/{repo}  ← clon compartido
///   {root}/work/task-{id}        ← worktree en la rama de trabajo
///   {root}/attachments/task-{id} ← adjuntos descargados
/// </summary>
public sealed partial class GitWorkspace(IOptions<WorkerOptions> options)
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> RepoLocks = new();
    private readonly WorkerOptions _options = options.Value;

    public async Task<PreparedWorkspace> PrepareAsync(AgentJob job, Action<string> log, CancellationToken ct)
    {
        ValidateBranch(job.BaseBranch);
        ValidateBranch(job.WorkBranch);

        var root = _options.Workspace.ResolveRoot();
        var repoDir = Path.Combine(root, "repos", SafeSegment(job.Repo.Owner), SafeSegment(job.Repo.Name));
        var workDir = Path.Combine(root, "work", $"task-{job.TaskId}");
        var attachDir = Path.Combine(root, "attachments", $"task-{job.TaskId}");

        var gate = RepoLocks.GetOrAdd(repoDir, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (!Directory.Exists(Path.Combine(repoDir, ".git")))
            {
                if (Directory.Exists(repoDir)) DeleteDirectory(repoDir);
                Directory.CreateDirectory(Path.GetDirectoryName(repoDir)!);
                log($"$ git clone {job.Repo.CloneUrl}");
                await GitAsync(root, ct, withAuth: job.Repo.CloneUrl, "clone", "--no-checkout", job.Repo.CloneUrl, repoDir);
            }

            await GitAsync(repoDir, ct, null, "config", "user.name", _options.Git.AuthorName);
            await GitAsync(repoDir, ct, null, "config", "user.email", _options.Git.AuthorEmail);

            log("$ git fetch origin --prune");
            await GitAsync(repoDir, ct, withAuth: job.Repo.CloneUrl, "fetch", "origin", "--prune");

            if (!await RefExistsAsync(repoDir, $"refs/remotes/origin/{job.BaseBranch}", ct))
                throw new GitException($"La rama base «{job.BaseBranch}» no existe en el repositorio remoto.");

            var remoteExists = await RefExistsAsync(repoDir, $"refs/remotes/origin/{job.WorkBranch}", ct);
            var startPoint = remoteExists ? $"origin/{job.WorkBranch}" : $"origin/{job.BaseBranch}";

            if (await IsWorktreeAsync(workDir, ct))
            {
                log($"$ git checkout -B {job.WorkBranch} {startPoint}  (worktree existente)");
                await GitAsync(workDir, ct, null, "reset", "--hard");
                await GitAsync(workDir, ct, null, "clean", "-fd");
                await GitAsync(workDir, ct, null, "checkout", "-B", job.WorkBranch, startPoint);
            }
            else
            {
                if (Directory.Exists(workDir)) DeleteDirectory(workDir);
                await GitAsync(repoDir, ct, null, "worktree", "prune");
                Directory.CreateDirectory(Path.GetDirectoryName(workDir)!);
                log($"$ git worktree add -B {job.WorkBranch} {workDir} {startPoint}");
                await GitAsync(repoDir, ct, null, "worktree", "add", "-f", "-B", job.WorkBranch, workDir, startPoint);
            }

            var head = (await GitAsync(workDir, ct, null, "rev-parse", "HEAD")).StdOut.Trim();
            log(remoteExists
                ? $"Rama {job.WorkBranch} retomada desde origin ({Short(head)})."
                : $"Rama {job.WorkBranch} creada desde {job.BaseBranch} ({Short(head)}).");

            if (Directory.Exists(attachDir)) DeleteDirectory(attachDir);
            Directory.CreateDirectory(attachDir);

            return new PreparedWorkspace(workDir, attachDir, head, remoteExists);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Arreglo de conflictos del pull request: integra <c>origin/{base}</c> en la rama de trabajo. Si hay conflictos deja
    /// el merge a medias (archivos con marcadores) para que el agente los resuelva y devuelve los archivos en conflicto;
    /// si la integración es limpia, git crea el commit del merge y devuelve una lista vacía.
    /// </summary>
    public async Task<IReadOnlyList<string>> MergeBaseAsync(
        AgentJob job, PreparedWorkspace ws, Action<string> log, CancellationToken ct)
    {
        var wt = ws.WorktreePath;
        var source = $"origin/{job.BaseBranch}";
        log($"$ git merge --no-edit {source}");
        var merge = await RunGitAsync(wt, ct, null, "merge", "--no-edit", "-m",
            $"Merge {source} en {job.WorkBranch} (DevArv #{job.TaskId}: conflictos del pull request)", source);

        if (merge.Ok)
        {
            log($"{source} se integró sin conflictos.");
            return [];
        }

        var conflicted = await ConflictedFilesAsync(wt, ct);
        if (conflicted.Count == 0)
        {
            var err = string.IsNullOrWhiteSpace(merge.StdErr) ? merge.StdOut : merge.StdErr;
            throw new GitException($"git merge falló ({merge.ExitCode}): {Redact(err.Trim())}");
        }

        log($"Conflictos en {conflicted.Count} archivo(s): {string.Join(", ", conflicted)}");
        return conflicted;
    }

    private async Task<List<string>> ConflictedFilesAsync(string wt, CancellationToken ct)
    {
        var r = await GitAsync(wt, ct, null, "diff", "--name-only", "--diff-filter=U");
        return r.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    /// <summary>Commit de lo pendiente y push de la rama de trabajo.</summary>
    /// <param name="conflictedFiles">
    /// Archivos que quedaron en conflicto al integrar la rama base (<see cref="MergeBaseAsync"/>): si alguno conserva
    /// marcadores de conflicto no se hace commit ni push.
    /// </param>
    public async Task<PushResult> CommitAndPushAsync(AgentJob job, PreparedWorkspace ws, Action<string> log, CancellationToken ct,
        IReadOnlyCollection<string>? conflictedFiles = null)
    {
        var wt = ws.WorktreePath;

        var branch = (await GitAsync(wt, ct, null, "rev-parse", "--abbrev-ref", "HEAD")).StdOut.Trim();
        if (!string.Equals(branch, job.WorkBranch, StringComparison.Ordinal))
            throw new GitException($"El agente cambió de rama ({branch}); se esperaba {job.WorkBranch}. No se hizo push.");

        await GitAsync(wt, ct, null, "add", "-A");

        if (conflictedFiles is { Count: > 0 })
        {
            // --check marca las líneas agregadas que parecen marcadores de conflicto (también avisa de espacios al final,
            // que se ignoran); sólo cuentan los archivos que estaban en conflicto. Se compara contra el commit inicial
            // para incluir lo que el agente haya confirmado por su cuenta.
            var check = await RunGitAsync(wt, ct, null, "diff", "--cached", "--check", ws.StartCommit);
            var leftovers = LeftoverConflictMarkers(check.StdOut, conflictedFiles);
            if (leftovers.Count > 0)
                throw new GitException("Quedan marcadores de conflicto sin resolver; no se hizo commit ni push: "
                                       + string.Join(", ", leftovers.Take(10)) + (leftovers.Count > 10 ? "…" : ""));
        }

        var staged = await RunGitAsync(wt, ct, null, "diff", "--cached", "--quiet");
        // Un merge a medias se cierra con su commit aunque la resolución no cambie archivos respecto a HEAD.
        var mergeInProgress = (await RunGitAsync(wt, ct, null, "rev-parse", "-q", "--verify", "MERGE_HEAD")).Ok;
        if (staged.ExitCode == 1 || mergeInProgress)
        {
            log("$ git commit");
            await GitAsync(wt, ct, null, "commit", "-m", job.CommitMessage,
                "-m", $"Etapa: {job.StageName}\nGenerado por DevArv con Claude Code.");
        }

        var head = (await GitAsync(wt, ct, null, "rev-parse", "HEAD")).StdOut.Trim();
        var changed = await ChangedFilesAsync(wt, job.BaseBranch, ct);

        var pushed = false;
        if (head != ws.StartCommit || (!ws.RemoteBranchExisted && changed.Count > 0))
        {
            log($"$ git push origin HEAD:refs/heads/{job.WorkBranch}");
            await GitAsync(wt, ct, withAuth: job.Repo.CloneUrl, "push", "origin", $"HEAD:refs/heads/{job.WorkBranch}");
            pushed = true;
        }
        else
        {
            log("Sin commits nuevos; no se hace push.");
        }

        log($"{changed.Count} archivo(s) distintos respecto a origin/{job.BaseBranch}.");
        return new PushResult(head, pushed, changed);
    }

    public async Task<string?> HeadAsync(PreparedWorkspace ws, CancellationToken ct)
    {
        var r = await RunGitAsync(ws.WorktreePath, ct, null, "rev-parse", "HEAD");
        return r.Ok ? r.StdOut.Trim() : null;
    }

    private async Task<List<ChangedFile>> ChangedFilesAsync(string wt, string baseBranch, CancellationToken ct)
    {
        var r = await GitAsync(wt, ct, null, "diff", "--name-status", "-M", $"origin/{baseBranch}...HEAD");
        return ParseNameStatus(r.StdOut);
    }

    internal static List<ChangedFile> ParseNameStatus(string output)
    {
        var files = new List<ChangedFile>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split('\t');
            if (parts.Length < 2) continue;
            var status = parts[0].Length > 0 ? parts[0][..1] : "?";
            files.Add(new ChangedFile(status, parts[^1]));
        }
        return files;
    }

    /// <summary>
    /// Ubicaciones (<c>archivo:línea</c>) de la salida de <c>git diff --check</c> que son marcadores de conflicto, sólo de
    /// los archivos indicados.
    /// </summary>
    internal static List<string> LeftoverConflictMarkers(string checkOutput, IReadOnlyCollection<string> files)
    {
        var result = new List<string>();
        foreach (var line in checkOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            const string marker = ": leftover conflict marker";
            if (!line.EndsWith(marker, StringComparison.Ordinal)) continue;
            var location = line[..^marker.Length];
            var colon = location.LastIndexOf(':');
            if (colon <= 0) continue;
            if (files.Contains(location[..colon])) result.Add(location);
        }
        return result;
    }

    // ------------------------------------------------------------------ helpers de git

    private async Task<bool> RefExistsAsync(string repoDir, string reference, CancellationToken ct) =>
        (await RunGitAsync(repoDir, ct, null, "rev-parse", "--verify", "--quiet", reference)).Ok;

    private async Task<bool> IsWorktreeAsync(string dir, CancellationToken ct)
    {
        if (!Directory.Exists(dir) || !File.Exists(Path.Combine(dir, ".git"))) return false;
        var r = await RunGitAsync(dir, ct, null, "rev-parse", "--is-inside-work-tree");
        return r.Ok && r.StdOut.Trim() == "true";
    }

    private async Task<ProcessResult> GitAsync(string cwd, CancellationToken ct, string? withAuth, params string[] args)
    {
        var r = await RunGitAsync(cwd, ct, withAuth, args);
        if (!r.Ok)
        {
            var err = string.IsNullOrWhiteSpace(r.StdErr) ? r.StdOut : r.StdErr;
            throw new GitException($"git {args[0]} falló ({r.ExitCode}): {Redact(err.Trim())}");
        }
        return r;
    }

    private Task<ProcessResult> RunGitAsync(string cwd, CancellationToken ct, string? withAuth, params string[] args)
    {
        var all = new List<string>();
        if (withAuth is not null)
        {
            var token = _options.Git.GitHubToken;
            if (string.IsNullOrWhiteSpace(token))
                throw new GitException("Falta Worker:Git:GitHubToken en el appsettings del worker.");
            if (!Uri.TryCreate(withAuth, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                throw new GitException($"Sólo se admiten repositorios por HTTPS: «{withAuth}».");

            // Igual que actions/checkout: la credencial va en una cabecera y nunca se guarda en .git/config.
            // credential.helper vacío evita que git use credenciales guardadas en la PC.
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"x-access-token:{token.Trim()}"));
            all.AddRange(["-c", "credential.helper=", "-c", $"http.https://{uri.Host}/.extraheader=AUTHORIZATION: basic {basic}"]);
        }
        all.AddRange(args);

        var env = new Dictionary<string, string>
        {
            ["GIT_TERMINAL_PROMPT"] = "0",
            ["GCM_INTERACTIVE"] = "never",
            ["LC_ALL"] = "C",
        };
        return ProcessRunner.RunAsync(new ProcessSpec(_options.Git.Executable, all, cwd) { Environment = env }, ct);
    }

    private string Redact(string text)
    {
        var token = _options.Git.GitHubToken;
        if (!string.IsNullOrWhiteSpace(token)) text = text.Replace(token.Trim(), "***");
        return BasicAuth().Replace(text, "basic ***");
    }

    private static void ValidateBranch(string branch)
    {
        if (string.IsNullOrWhiteSpace(branch) || branch.StartsWith('-') || branch.Contains("..")
            || !BranchName().IsMatch(branch))
            throw new GitException($"Nombre de rama inválido: «{branch}».");
    }

    private static string SafeSegment(string value) => InvalidSegment().Replace(value, "_");

    private static string Short(string sha) => sha.Length > 10 ? sha[..10] : sha;

    internal static void DeleteDirectory(string path)
    {
        // En Windows git marca objetos como sólo lectura; hay que quitar el atributo antes de borrar.
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            try { File.SetAttributes(file, FileAttributes.Normal); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        Directory.Delete(path, recursive: true);
    }

    [GeneratedRegex(@"^[A-Za-z0-9._/\-]+$")]
    private static partial Regex BranchName();

    [GeneratedRegex(@"[^A-Za-z0-9._\-]")]
    private static partial Regex InvalidSegment();

    [GeneratedRegex(@"basic [A-Za-z0-9+/=]{16,}", RegexOptions.IgnoreCase)]
    private static partial Regex BasicAuth();
}
