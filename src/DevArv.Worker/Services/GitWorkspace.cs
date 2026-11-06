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
public sealed partial class GitWorkspace(IOptions<WorkerOptions> Options)
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _RepoLocks = new();
    private readonly WorkerOptions _Options = Options.Value;

    public async Task<PreparedWorkspace> PrepareAsync(AgentJob Job, Action<string> Log, CancellationToken CT)
    {
        ValidateBranch(Job.BaseBranch);
        ValidateBranch(Job.WorkBranch);

        var root = _Options.Workspace.ResolveRoot();
        var repoDir = Path.Combine(root, "repos", SafeSegment(Job.Repo.Owner), SafeSegment(Job.Repo.Name));
        var workDir = Path.Combine(root, "work", $"task-{Job.TaskID}");
        var attachDir = Path.Combine(root, "attachments", $"task-{Job.TaskID}");

        var gate = _RepoLocks.GetOrAdd(repoDir, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(CT);
        try
        {
            if (!Directory.Exists(Path.Combine(repoDir, ".git")))
            {
                if (Directory.Exists(repoDir)) DeleteDirectory(repoDir);
                Directory.CreateDirectory(Path.GetDirectoryName(repoDir)!);
                Log($"$ git clone {Job.Repo.CloneUrl}");
                await GitAsync(
                    Cwd: root,
                    Args: ["clone", "--no-checkout", Job.Repo.CloneUrl, repoDir],
                    CT: CT,
                    WithAuth: Job.Repo.CloneUrl);
            }

            await GitAsync(
                Cwd: repoDir,
                Args: ["config", "user.name", _Options.Git.AuthorName],
                CT: CT);
            await GitAsync(
                Cwd: repoDir,
                Args: ["config", "user.email", _Options.Git.AuthorEmail],
                CT: CT);

            Log("$ git fetch origin --prune");
            await GitAsync(
                Cwd: repoDir,
                Args: ["fetch", "origin", "--prune"],
                CT: CT,
                WithAuth: Job.Repo.CloneUrl);

            var baseExists = await RefExistsAsync(
                RepoDir: repoDir,
                Reference: $"refs/remotes/origin/{Job.BaseBranch}",
                CT: CT);
            if (!baseExists)
                throw new GitException($"La rama base «{Job.BaseBranch}» no existe en el repositorio remoto.");

            var remoteExists = await RefExistsAsync(
                RepoDir: repoDir,
                Reference: $"refs/remotes/origin/{Job.WorkBranch}",
                CT: CT);
            var startPoint = remoteExists ? $"origin/{Job.WorkBranch}" : $"origin/{Job.BaseBranch}";

            if (await IsWorktreeAsync(workDir, CT))
            {
                Log($"$ git checkout -B {Job.WorkBranch} {startPoint}  (worktree existente)");
                await GitAsync(
                    Cwd: workDir,
                    Args: ["reset", "--hard"],
                    CT: CT);
                await GitAsync(
                    Cwd: workDir,
                    Args: ["clean", "-fd"],
                    CT: CT);
                await GitAsync(
                    Cwd: workDir,
                    Args: ["checkout", "-B", Job.WorkBranch, startPoint],
                    CT: CT);
            }
            else
            {
                if (Directory.Exists(workDir)) DeleteDirectory(workDir);
                await GitAsync(
                    Cwd: repoDir,
                    Args: ["worktree", "prune"],
                    CT: CT);
                Directory.CreateDirectory(Path.GetDirectoryName(workDir)!);
                Log($"$ git worktree add -B {Job.WorkBranch} {workDir} {startPoint}");
                await GitAsync(
                    Cwd: repoDir,
                    Args: ["worktree", "add", "-f", "-B", Job.WorkBranch, workDir, startPoint],
                    CT: CT);
            }

            var head = (await GitAsync(
                Cwd: workDir,
                Args: ["rev-parse", "HEAD"],
                CT: CT)).StdOut.Trim();
            Log(remoteExists
                ? $"Rama {Job.WorkBranch} retomada desde origin ({Short(head)})."
                : $"Rama {Job.WorkBranch} creada desde {Job.BaseBranch} ({Short(head)}).");

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
    public async Task<IReadOnlyList<string>> MergeBaseAsync(AgentJob Job, PreparedWorkspace Workspace, Action<string> Log, CancellationToken CT)
    {
        var worktree = Workspace.WorktreePath;
        var source = $"origin/{Job.BaseBranch}";
        Log($"$ git merge --no-edit {source}");
        var merge = await RunGitAsync(
            Cwd: worktree,
            Args: ["merge", "--no-edit", "-m", $"Merge {source} en {Job.WorkBranch} (DevArv #{Job.TaskID}: conflictos del pull request)", source],
            CT: CT);

        if (merge.Ok)
        {
            Log($"{source} se integró sin conflictos.");
            return [];
        }

        var conflicted = await ConflictedFilesAsync(
            Worktree: worktree,
            CT: CT);
        if (conflicted.Count == 0)
        {
            var err = string.IsNullOrWhiteSpace(merge.StdErr) ? merge.StdOut : merge.StdErr;
            throw new GitException($"git merge falló ({merge.ExitCode}): {Redact(err.Trim())}");
        }

        Log($"Conflictos en {conflicted.Count} archivo(s): {string.Join(", ", conflicted)}");
        return conflicted;
    }

    private async Task<List<string>> ConflictedFilesAsync(string Worktree, CancellationToken CT)
    {
        // -z separa con NUL y no entrecomilla ni escapa nombres con caracteres no ASCII (español.txt), igual que
        // las rutas que imprime «git diff --check», con las que luego se comparan.
        var r = await GitAsync(
            Cwd: Worktree,
            Args: ["diff", "--name-only", "-z", "--diff-filter=U"],
            CT: CT);
        return ParseNulSeparated(r.StdOut);
    }

    /// <summary>Nombres separados por NUL. Git añade un «\n» suelto tras las rutas sin fusionar: se descarta.</summary>
    internal static List<string> ParseNulSeparated(string Output) =>
        Output.Split('\0', StringSplitOptions.RemoveEmptyEntries).Where(name => !string.IsNullOrWhiteSpace(name)).ToList();

    /// <summary>Commit de lo pendiente y push de la rama de trabajo.</summary>
    /// <param name="ConflictedFiles">
    /// Archivos que quedaron en conflicto al integrar la rama base (<see cref="MergeBaseAsync"/>): si alguno conserva
    /// marcadores de conflicto no se hace commit ni push.
    /// </param>
    public async Task<PushResult> CommitAndPushAsync(
        AgentJob Job, PreparedWorkspace Workspace, Action<string> Log, CancellationToken CT,
        IReadOnlyCollection<string>? ConflictedFiles = null)
    {
        var worktree = Workspace.WorktreePath;

        var branch = (await GitAsync(
            Cwd: worktree,
            Args: ["rev-parse", "--abbrev-ref", "HEAD"],
            CT: CT)).StdOut.Trim();
        if (!string.Equals(branch, Job.WorkBranch, StringComparison.Ordinal))
            throw new GitException($"El agente cambió de rama ({branch}); se esperaba {Job.WorkBranch}. No se hizo push.");

        await GitAsync(
            Cwd: worktree,
            Args: ["add", "-A"],
            CT: CT);

        if (ConflictedFiles is { Count: > 0 })
        {
            // --check marca las líneas agregadas que parecen marcadores de conflicto (también avisa de espacios al final,
            // que se ignoran); sólo cuentan los archivos que estaban en conflicto. Se compara contra el commit inicial
            // para incluir lo que el agente haya confirmado por su cuenta.
            var check = await RunGitAsync(
                Cwd: worktree,
                Args: ["diff", "--cached", "--check", Workspace.StartCommit],
                CT: CT);
            var leftovers = LeftoverConflictMarkers(
                CheckOutput: check.StdOut,
                Files: ConflictedFiles);
            if (leftovers.Count > 0)
                throw new GitException("Quedan marcadores de conflicto sin resolver; no se hizo commit ni push: "
                                       + string.Join(", ", leftovers.Take(10)) + (leftovers.Count > 10 ? "…" : ""));
        }

        var staged = await RunGitAsync(
            Cwd: worktree,
            Args: ["diff", "--cached", "--quiet"],
            CT: CT);
        // Un merge a medias se cierra con su commit aunque la resolución no cambie archivos respecto a HEAD.
        var mergeInProgress = (await RunGitAsync(
            Cwd: worktree,
            Args: ["rev-parse", "-q", "--verify", "MERGE_HEAD"],
            CT: CT)).Ok;
        if (staged.ExitCode == 1 || mergeInProgress)
        {
            Log("$ git commit");
            await GitAsync(
                Cwd: worktree,
                Args: ["commit", "-m", Job.CommitMessage, "-m", $"Etapa: {Job.StageName}\nGenerado por DevArv con Claude Code."],
                CT: CT);
        }

        var head = (await GitAsync(
            Cwd: worktree,
            Args: ["rev-parse", "HEAD"],
            CT: CT)).StdOut.Trim();
        var changed = await ChangedFilesAsync(
            Worktree: worktree,
            BaseBranch: Job.BaseBranch,
            CT: CT);

        var pushed = false;
        if (head != Workspace.StartCommit || (!Workspace.RemoteBranchExisted && changed.Count > 0))
        {
            Log($"$ git push origin HEAD:refs/heads/{Job.WorkBranch}");
            await GitAsync(
                Cwd: worktree,
                Args: ["push", "origin", $"HEAD:refs/heads/{Job.WorkBranch}"],
                CT: CT,
                WithAuth: Job.Repo.CloneUrl);
            pushed = true;
        }
        else
        {
            Log("Sin commits nuevos; no se hace push.");
        }

        Log($"{changed.Count} archivo(s) distintos respecto a origin/{Job.BaseBranch}.");
        return new PushResult(head, pushed, changed);
    }

    public async Task<string?> HeadAsync(PreparedWorkspace Workspace, CancellationToken CT)
    {
        var r = await RunGitAsync(
            Cwd: Workspace.WorktreePath,
            Args: ["rev-parse", "HEAD"],
            CT: CT);
        return r.Ok ? r.StdOut.Trim() : null;
    }

    private async Task<List<ChangedFile>> ChangedFilesAsync(string Worktree, string BaseBranch, CancellationToken CT)
    {
        var r = await GitAsync(
            Cwd: Worktree,
            Args: ["diff", "--name-status", "-M", $"origin/{BaseBranch}...HEAD"],
            CT: CT);
        return ParseNameStatus(r.StdOut);
    }

    /// <summary>
    /// Ubicaciones (<c>archivo:línea</c>) de la salida de <c>git diff --check</c> que son marcadores de conflicto, sólo de
    /// los archivos indicados.
    /// </summary>
    internal static List<string> LeftoverConflictMarkers(string CheckOutput, IReadOnlyCollection<string> Files)
    {
        var result = new List<string>();
        foreach (var line in CheckOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            const string marker = ": leftover conflict marker";
            if (!line.EndsWith(marker, StringComparison.Ordinal)) continue;
            var location = line[..^marker.Length];
            var colon = location.LastIndexOf(':');
            if (colon <= 0) continue;
            if (Files.Contains(location[..colon])) result.Add(location);
        }
        return result;
    }

    internal static List<ChangedFile> ParseNameStatus(string Output)
    {
        var files = new List<ChangedFile>();
        foreach (var line in Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split('\t');
            if (parts.Length < 2) continue;
            var status = parts[0].Length > 0 ? parts[0][..1] : "?";
            files.Add(new ChangedFile(status, parts[^1]));
        }
        return files;
    }

    // ------------------------------------------------------------------ helpers de git

    private async Task<bool> RefExistsAsync(string RepoDir, string Reference, CancellationToken CT) =>
        (await RunGitAsync(
            Cwd: RepoDir,
            Args: ["rev-parse", "--verify", "--quiet", Reference],
            CT: CT)).Ok;

    private async Task<bool> IsWorktreeAsync(string Dir, CancellationToken CT)
    {
        if (!Directory.Exists(Dir) || !File.Exists(Path.Combine(Dir, ".git"))) return false;
        var r = await RunGitAsync(
            Cwd: Dir,
            Args: ["rev-parse", "--is-inside-work-tree"],
            CT: CT);
        return r.Ok && r.StdOut.Trim() == "true";
    }

    private async Task<ProcessResult> GitAsync(string Cwd, IReadOnlyList<string> Args, CancellationToken CT, string? WithAuth = null)
    {
        var r = await RunGitAsync(
            Cwd: Cwd,
            Args: Args,
            CT: CT,
            WithAuth: WithAuth);
        if (!r.Ok)
        {
            var err = string.IsNullOrWhiteSpace(r.StdErr) ? r.StdOut : r.StdErr;
            throw new GitException($"git {Args[0]} falló ({r.ExitCode}): {Redact(err.Trim())}");
        }
        return r;
    }

    private Task<ProcessResult> RunGitAsync(string Cwd, IReadOnlyList<string> Args, CancellationToken CT, string? WithAuth = null)
    {
        var all = new List<string>();
        if (WithAuth is not null)
        {
            var token = _Options.Git.GitHubToken;
            if (string.IsNullOrWhiteSpace(token))
                throw new GitException("Falta Worker:Git:GitHubToken en el appsettings del worker.");
            if (!Uri.TryCreate(WithAuth, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                throw new GitException($"Sólo se admiten repositorios por HTTPS: «{WithAuth}».");

            // Igual que actions/checkout: la credencial va en una cabecera y nunca se guarda en .git/config.
            // credential.helper vacío evita que git use credenciales guardadas en la PC.
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"x-access-token:{token.Trim()}"));
            all.AddRange(["-c", "credential.helper=", "-c", $"http.https://{uri.Host}/.extraheader=AUTHORIZATION: basic {basic}"]);
        }
        all.AddRange(Args);

        var env = new Dictionary<string, string>
        {
            ["GIT_TERMINAL_PROMPT"] = "0",
            ["GCM_INTERACTIVE"] = "never",
            ["LC_ALL"] = "C",
        };
        return ProcessRunner.RunAsync(new ProcessSpec(_Options.Git.Executable, all, Cwd) { Environment = env }, CT);
    }

    private string Redact(string Text)
    {
        var token = _Options.Git.GitHubToken;
        if (!string.IsNullOrWhiteSpace(token)) Text = Text.Replace(token.Trim(), "***");
        return BasicAuth().Replace(Text, "basic ***");
    }

    private static void ValidateBranch(string Branch)
    {
        if (string.IsNullOrWhiteSpace(Branch) || Branch.StartsWith('-') || Branch.Contains("..")
            || !BranchName().IsMatch(Branch))
            throw new GitException($"Nombre de rama inválido: «{Branch}».");
    }

    private static string SafeSegment(string Value) => InvalidSegment().Replace(Value, "_");

    private static string Short(string Sha) => Sha.Length > 10 ? Sha[..10] : Sha;

    internal static void DeleteDirectory(string Path)
    {
        // En Windows git marca objetos como sólo lectura; hay que quitar el atributo antes de borrar.
        foreach (var file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
        {
            try { File.SetAttributes(file, FileAttributes.Normal); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        Directory.Delete(Path, recursive: true);
    }

    [GeneratedRegex(@"^[A-Za-z0-9._/\-]+$")]
    private static partial Regex BranchName();

    [GeneratedRegex(@"[^A-Za-z0-9._\-]")]
    private static partial Regex InvalidSegment();

    [GeneratedRegex(@"basic [A-Za-z0-9+/=]{16,}", RegexOptions.IgnoreCase)]
    private static partial Regex BasicAuth();
}
