using Microsoft.Extensions.Configuration.CommandLine;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;

namespace DevArv.Worker.Helpers;

/// <summary>
/// Deja los appsettings como única fuente de la configuración de la app:
/// appsettings.json → appsettings.{Entorno}.json → appsettings.Local.json (cada uno sobrescribe al anterior).
/// Se quitan user-secrets, variables de entorno y argumentos de línea de comandos. Sólo se conservan las variables
/// del host con prefijo ASPNETCORE_ o DOTNET_ (entorno, URLs, IIS), y los appsettings van después de ellas,
/// así que cualquier clave definida en un appsettings no se puede sobrescribir desde fuera.
/// </summary>
public static class AppSettingsConfiguration
{
    public const string LocalFile = "appsettings.Local.json";

    private static readonly string[] HostPrefixes = ["ASPNETCORE_", "DOTNET_"];

    public static void UseAppSettingsOnly(this IConfigurationBuilder configuration, string environmentName)
    {
        var sources = configuration.Sources;
        for (var i = sources.Count - 1; i >= 0; i--)
        {
            if (IsReplaced(sources[i])) sources.RemoveAt(i);
        }

        configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
        configuration.AddJsonFile($"appsettings.{environmentName}.json", optional: true, reloadOnChange: true);
        configuration.AddJsonFile(LocalFile, optional: true, reloadOnChange: true);
    }

    // Los JSON (appsettings y secrets.json de user-secrets) se vuelven a agregar al final, sin user-secrets.
    private static bool IsReplaced(IConfigurationSource source) => source switch
    {
        JsonConfigurationSource => true,
        CommandLineConfigurationSource => true,
        EnvironmentVariablesConfigurationSource env => !HostPrefixes.Contains(env.Prefix),
        _ => false,
    };
}
