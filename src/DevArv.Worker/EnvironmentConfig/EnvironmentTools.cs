using Microsoft.Extensions.Configuration.CommandLine;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;

namespace DevArv.Worker.EnvironmentConfig;

/// <summary>
/// Única fuente de configuración de la aplicación: appsettings.json → appsettings.{Entorno}.json → appsettings.Local.json
/// (cada uno sobrescribe al anterior; las credenciales van en appsettings.Local.json, ignorado por git).
/// No se leen user-secrets, variables de entorno ni argumentos de línea de comandos.
/// </summary>
public static class EnvironmentTools
{
    public const string SectionName = "EnvironmentData";
    public const string LocalFile = "appsettings.Local.json";

    private static readonly string[] HostPrefixes = ["ASPNETCORE_", "DOTNET_"];

    // Configuración cargada una sola vez al iniciar la aplicación, sin recargas por cambio de archivo.
    private static readonly IConfigurationRoot Root = Build(ResolveBasePath(), GetEnvironmentName());

    /// <summary>Configuración cargada desde los appsettings.</summary>
    public static IConfiguration Configuration => Root;

    /// <summary>Retorna los datos de conexión al servidor DevArv (sección EnvironmentData).</summary>
    public static EnvironmentData GetData() => GetSectionData(Root, SectionName);

    /// <summary>
    /// Deja la configuración de EnvironmentTools como fuente de la configuración del host (Options, logging, etc.).
    /// Sólo se conservan las variables del host ASPNETCORE_/DOTNET_ (entorno, URLs, IIS), que van antes y no pisan
    /// ninguna clave definida en los appsettings.
    /// </summary>
    public static void UseAsConfigurationSource(IConfigurationBuilder builder) => ReplaceSources(builder, Root);

    internal static void ReplaceSources(IConfigurationBuilder builder, IConfiguration configuration)
    {
        var sources = builder.Sources;
        for (var i = sources.Count - 1; i >= 0; i--)
        {
            if (IsReplaced(sources[i])) sources.RemoveAt(i);
        }
        builder.AddConfiguration(configuration, shouldDisposeConfiguration: false);
    }

    internal static IConfigurationRoot Build(string basePath, string environmentName) => new ConfigurationBuilder()
        .SetBasePath(basePath)
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
        .AddJsonFile($"appsettings.{environmentName}.json", optional: true, reloadOnChange: false)
        .AddJsonFile(LocalFile, optional: true, reloadOnChange: false)
        .Build();

    // Lee y valida la sección de datos de entorno.
    internal static EnvironmentData GetSectionData(IConfiguration configuration, string sectionName)
    {
        var section = configuration.GetSection(sectionName);

        // GetSection() nunca retorna null: retorna una sección vacía si la clave no existe.
        if (!section.Exists())
            throw new ArgumentException($"La sección de configuración '{sectionName}' no existe en appsettings.");

        // La URL del servidor debe ser absoluta y http(s).
        if (!Uri.TryCreate(section["Url"]?.Trim(), UriKind.Absolute, out var url)
            || (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp))
            throw new ArgumentException($"{sectionName}:Url debe ser la URL http(s) del servidor DevArv.");

        if (string.IsNullOrWhiteSpace(section["ApiKey"]))
            throw new ArgumentException($"{sectionName}:ApiKey no puede estar vacío (clave generada en «Mis PCs»).");

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
    private static bool IsReplaced(IConfigurationSource source) => source switch
    {
        JsonConfigurationSource => true,
        CommandLineConfigurationSource => true,
        EnvironmentVariablesConfigurationSource env => !HostPrefixes.Contains(env.Prefix),
        _ => false,
    };
}
