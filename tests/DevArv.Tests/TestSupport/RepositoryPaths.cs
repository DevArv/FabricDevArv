namespace DevArv.Tests.TestSupport;

/// <summary>Rutas del código fuente, para las pruebas que revisan archivos que no se compilan (vistas, CSS).</summary>
public static class RepositoryPaths
{
    public static string Root { get; } = FindRoot();

    public static string WebProject => Path.Combine(Root, "src", "DevArv.Web");

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DevArv.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio (DevArv.slnx).");
    }
}
