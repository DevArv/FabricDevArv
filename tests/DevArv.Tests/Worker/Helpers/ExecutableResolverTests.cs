using DevArv.Worker.Helpers;

namespace DevArv.Tests.Worker.Helpers;

public sealed class ExecutableResolverTests : IDisposable
{
    private readonly string _Root = Directory.CreateTempSubdirectory("devarv-path-").FullName;

    public void Dispose() => Directory.Delete(_Root, recursive: true);

    private string CreateDirectory(string Name)
    {
        var directory = Path.Combine(_Root, Name);
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string CreateFile(string Directory, string Name)
    {
        var path = Path.Combine(Directory, Name);
        File.WriteAllText(path, "");
        return path;
    }

    [Fact]
    public void FindOnPath_RootedPath_IsReturnedOnlyWhenTheFileExists()
    {
        var existing = CreateFile(CreateDirectory("a"), "claude.exe");

        Assert.Equal(existing, ExecutableResolver.FindOnPath(
            Executable: existing,
            Directories: [],
            Extensions: []));
        Assert.Null(ExecutableResolver.FindOnPath(
            Executable: Path.Combine(_Root, "no-existe.exe"),
            Directories: [],
            Extensions: []));
    }

    [Fact]
    public void FindOnPath_NameWithoutExtension_TriesEachExtensionInOrder()
    {
        var directory = CreateDirectory("bin");
        CreateFile(directory, "claude.BAT");
        var cmd = CreateFile(directory, "claude.CMD");

        var found = ExecutableResolver.FindOnPath(
            Executable: "claude",
            Directories: [directory],
            Extensions: [".EXE", ".CMD", ".BAT"]);

        Assert.Equal(cmd, found);
    }

    [Fact]
    public void FindOnPath_EarlierDirectoriesWin()
    {
        var first = CreateFile(CreateDirectory("uno"), "git.EXE");
        CreateFile(CreateDirectory("dos"), "git.EXE");

        var found = ExecutableResolver.FindOnPath(
            Executable: "git",
            Directories: [Path.GetDirectoryName(first)!, Path.Combine(_Root, "dos")],
            Extensions: [".EXE"]);

        Assert.Equal(first, found);
    }

    [Fact]
    public void FindOnPath_NameWithExtension_DoesNotAppendAnother()
    {
        var directory = CreateDirectory("bin");
        CreateFile(directory, "claude.cmd.EXE");
        var exact = CreateFile(directory, "claude.cmd");

        var found = ExecutableResolver.FindOnPath(
            Executable: "claude.cmd",
            Directories: [directory],
            Extensions: [".EXE"]);

        Assert.Equal(exact, found);
    }

    [Fact]
    public void FindOnPath_NotFound_ReturnsNull_AlsoWithoutDirectories()
    {
        var directory = CreateDirectory("bin");

        Assert.Null(ExecutableResolver.FindOnPath(
            Executable: "claude",
            Directories: [directory],
            Extensions: [".EXE"]));
        Assert.Null(ExecutableResolver.FindOnPath(
            Executable: "claude",
            Directories: [],
            Extensions: [".EXE"]));
    }
}
