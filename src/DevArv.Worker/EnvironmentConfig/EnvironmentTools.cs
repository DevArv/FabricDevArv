using Microsoft.Extensions.Configuration.CommandLine;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;

namespace DevArv.Worker.EnvironmentConfig;

/// <summary>
/// Única fuente de configuración de la aplicación: appsettings.json → appsettings.{Entorno}.json → appsettings.Local.json
/// (cada uno sobrescribe al anterior; las credenciales van en appsettings.Local.json, ignorado por git).
/// No se leen user-secrets, variables de entorno ni argumentos de línea de comandos para obtener configuración; las únicas
/// variables del sistema que se consultan (aquí) son ASPNETCORE_/DOTNET_ENVIRONMENT y el PATH para ubicar ejecutables.
/// </summary>
public static class EnvironmentTools
{
    public const string SECTION_NAME = "EnvironmentData";
    public const string LOCAL_FILE = "appsettings.Local.json";

    private static readonly string[] _HostPrefixes = ["ASPNETCORE_", "DOTNET_"];

    // Configuración cargada una sola vez al iniciar la aplicación, sin recargas por cambio de archivo.
    private static readonly IConfigurationRoot _Root = Build(ResolveBasePath(), GetEnvironmentName());

    /// <summary>Configuración cargada desde los appsettings.</summary>
    public static IConfiguration Configuration => _Root;

    /// <summary>Retorna los datos de conexión al servidor DevArv (sección EnvironmentData).</summary>
    public static EnvironmentData GetData() => GetSectionData(_Root, SECTION_NAME);

    /// <summary>Extensiones que Windows prueba al buscar un ejecutable sin extensión si <c>PATHEXT</c> no está definida.</summary>
    internal const string DEFAULT_PATHEXT = ".EXE;.CMD;.BAT";

    /// <summary>
    /// Directorios del <c>PATH</c> del sistema, donde se busca el ejecutable de Claude Code. No es configuración de la
    /// aplicación (esa sale de los appsettings): es el entorno del sistema operativo, y sólo esta clase lo lee.
    /// </summary>
    public static IReadOnlyList<string> GetExecutableSearchDirectories() =>
        ParseSearchDirectories(Environment.GetEnvironmentVariable("PATH"));

    /// <summary>Extensiones de ejecutable de Windows (<c>PATHEXT</c>), con un valor por defecto si no está definida.</summary>
    public static IReadOnlyList<string> GetExecutableExtensions() =>
        ParseExtensions(Environment.GetEnvironmentVariable("PATHEXT"));

    internal static IReadOnlyList<string> ParseSearchDirectories(string? Value) =>
        (Value ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => directory.Trim().Trim('"'))
            .Where(directory => directory.Length > 0)
            .ToList();

    internal static IReadOnlyList<string> ParseExtensions(string? Value) =>
        (string.IsNullOrWhiteSpace(Value) ? DEFAULT_PATHEXT : Value).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// Deja la configuración de EnvironmentTools como fuente de la configuración del host (Options, logging, etc.).
    /// Sólo se conservan las variables del host ASPNETCORE_/DOTNET_ (entorno, URLs, IIS), que van antes y no pisan
    /// ninguna clave definida en los appsettings.
    /// </summary>
    public static void UseAsConfigurationSource(IConfigurationBuilder Builder) => ReplaceSources(Builder, _Root);

    internal static void ReplaceSources(IConfigurationBuilder Builder, IConfiguration Settings)
    {
        var sources = Builder.Sources;
        for (var i = sources.Count - 1; i >= 0; i--)
        {
            if (IsReplaced(sources[i])) sources.RemoveAt(i);
        }
        Builder.AddConfiguration(Settings, shouldDisposeConfiguration: false);
    }

    internal static IConfigurationRoot Build(string BasePath, string EnvironmentName) => new ConfigurationBuilder()
        .SetBasePath(BasePath)
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
        .AddJsonFile($"appsettings.{EnvironmentName}.json", optional: true, reloadOnChange: false)
        .AddJsonFile(LOCAL_FILE, optional: true, reloadOnChange: false)
        .Build();

    // Lee y valida la sección de datos de entorno.
    internal static EnvironmentData GetSectionData(IConfiguration Settings, string SectionName)
    {
        var section = Settings.GetSection(SectionName);

        // GetSection() nunca retorna null: retorna una sección vacía si la clave no existe.
        if (!section.Exists())
            throw new ArgumentException($"La sección de configuración '{SectionName}' no existe en appsettings.");

        // La URL del servidor debe ser absoluta y http(s).
        if (!Uri.TryCreate(section["Url"]?.Trim(), UriKind.Absolute, out var url)
            || (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp))
            throw new ArgumentException($"{SectionName}:Url debe ser la URL http(s) del servidor DevArv.");

        if (string.IsNullOrWhiteSpace(section["ApiKey"]))
            throw new ArgumentException($"{SectionName}:ApiKey no puede estar vacío (clave generada en «Mis PCs»).");

        return new EnvironmentData
        {
            Url = section["Url"]!.Trim(),
            ApiKey = section["ApiKey"]!.Trim(),
        };
    }

    // El directorio actual (dotnet run, IIS); si no tiene appsettings.json (servicio de Windows arranca en System32),
    // la carpeta del ejecutable.
    private static string ResolveBasePath()
    {
        var current = Directory.GetCurrentDirectory();
        return File.Exists(Path.Combine(current, "appsettings.json")) ? current : AppContext.BaseDirectory;
    }

    // Mismo criterio que el host para elegir appsettings.{Entorno}.json.
    private static string GetEnvironmentName() =>
        Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
        ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
        ?? "Production";

    // Se quitan los JSON del host (incluido secrets.json de user-secrets), la línea de comandos y las variables
    // de entorno de la app; los appsettings llegan desde EnvironmentTools.
    private static bool IsReplaced(IConfigurationSource Source) => Source switch
    {
        JsonConfigurationSource => true,
        CommandLineConfigurationSource => true,
        EnvironmentVariablesConfigurationSource env => !_HostPrefixes.Contains(env.Prefix),
        _ => false,
    };
}
