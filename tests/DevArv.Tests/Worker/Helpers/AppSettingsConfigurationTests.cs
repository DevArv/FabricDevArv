using DevArv.Worker.Helpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.CommandLine;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;

namespace DevArv.Tests.Worker.Helpers;

public sealed class AppSettingsConfigurationTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("devarv-cfg-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private ConfigurationManager CreateDefaultLikeConfiguration(params string[] args)
    {
        File.WriteAllText(Path.Combine(_dir, "appsettings.json"), """{"GitHub":{"Token":""},"App":{"Name":"base"}}""");
        File.WriteAllText(Path.Combine(_dir, "appsettings.Development.json"), """{"App":{"Name":"dev"}}""");
        File.WriteAllText(Path.Combine(_dir, "secrets.json"), """{"GitHub":{"Token":"desde-user-secrets"},"Solo":{"Secreto":"x"}}""");
        File.WriteAllText(Path.Combine(_dir, AppSettingsConfiguration.LocalFile), """{"GitHub":{"Token":"desde-local"}}""");

        // Mismo orden de fuentes que arma el host por defecto.
        var configuration = new ConfigurationManager();
        configuration.SetBasePath(_dir);
        configuration.AddEnvironmentVariables(prefix: "DOTNET_");
        configuration.AddJsonFile("appsettings.json", optional: true);
        configuration.AddJsonFile("appsettings.Development.json", optional: true);
        configuration.AddJsonFile("secrets.json", optional: true);
        configuration.AddEnvironmentVariables();
        configuration.AddCommandLine(args);
        return configuration;
    }

    [Fact]
    public void LocalFileWinsOverCommandLineAndUserSecrets()
    {
        var configuration = CreateDefaultLikeConfiguration("--GitHub:Token=desde-cli", "--Solo:Cli=x");

        configuration.UseAppSettingsOnly("Development");

        Assert.Equal("desde-local", configuration["GitHub:Token"]);
        Assert.Equal("dev", configuration["App:Name"]);
        Assert.Null(configuration["Solo:Secreto"]);
        Assert.Null(configuration["Solo:Cli"]);
    }

    [Fact]
    public void ProcessEnvironmentVariablesAreIgnored()
    {
        var key = $"DevArvProbe{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable($"{key}__Valor", "desde-entorno");
        try
        {
            var configuration = CreateDefaultLikeConfiguration();

            configuration.UseAppSettingsOnly("Development");

            Assert.Null(configuration[$"{key}:Valor"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable($"{key}__Valor", null);
        }
    }

    [Fact]
    public void KeepsHostVariablesAndPutsAppSettingsLast()
    {
        var configuration = CreateDefaultLikeConfiguration();

        configuration.UseAppSettingsOnly("Production");

        var sources = ((IConfigurationBuilder)configuration).Sources;
        Assert.DoesNotContain(sources, s => s is CommandLineConfigurationSource);
        Assert.Single(sources.OfType<EnvironmentVariablesConfigurationSource>(), s => s.Prefix == "DOTNET_");
        Assert.DoesNotContain(sources.OfType<EnvironmentVariablesConfigurationSource>(), s => string.IsNullOrEmpty(s.Prefix));
        Assert.Equal(
            new[] { "appsettings.json", "appsettings.Production.json", AppSettingsConfiguration.LocalFile },
            sources.OfType<JsonConfigurationSource>().Select(s => s.Path));
        Assert.IsType<JsonConfigurationSource>(sources[^1]);
    }
}
