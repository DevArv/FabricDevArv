using Microsoft.Extensions.Configuration.CommandLine;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;

namespace DevArv.Web.EnvironmentConfig;

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

    /// <summary>Retorna los datos de conexión a la base de datos (sección EnvironmentData).</summary>
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

        if (string.IsNullOrWhiteSpace(section["Host"]))
            throw new ArgumentException($"{sectionName}:Host no puede estar vacío.");

        // El puerto debe ser un entero positivo en el rango válido de TCP (1–65535).
        if (!int.TryParse(section["Port"], out var port) || port < 1 || port > 65535)
            throw new ArgumentException($"{sectionName}:Port debe ser un número entero entre 1 y 65535.");

        if (string.IsNullOrWhiteSpace(section["Database"]))
            throw new ArgumentException($"{sectionName}:Database no puede estar vacío.");

        if (string.IsNullOrWhiteSpace(section["Username"]))
            throw new ArgumentException($"{sectionName}:Username no puede estar vacío.");

        if (string.IsNullOrWhiteSpace(section["Password"]))
            throw new ArgumentException($"{sectionName}:Password no puede estar vacío.");

        return new EnvironmentData
        {
            Host = section["Host"]!.Trim(),
            Port = section["Port"]!.Trim(),
            Database = section["Database"]!.Trim(),
            Username = section["Username"]!.Trim(),
            Password = section["Password"]!,
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
