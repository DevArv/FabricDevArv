using DevArv.Web.EnvironmentConfig;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.CommandLine;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;

namespace DevArv.Tests.Web.EnvironmentConfig;

public sealed class EnvironmentToolsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("devarv-env-").FullName;

    public EnvironmentToolsTests()
    {
        File.WriteAllText(Path.Combine(_dir, "appsettings.json"), """{"EnvironmentData":{"Host":"","Port":"","Database":"","Username":"","Password":""},"GitHub":{"Token":""},"App":{"Name":"base"}}""");
        File.WriteAllText(Path.Combine(_dir, "appsettings.Development.json"), """{"App":{"Name":"dev"}}""");
        File.WriteAllText(Path.Combine(_dir, EnvironmentTools.LocalFile), """{"EnvironmentData":{"Host":"db.local","Port":"5432","Database":"devarv","Username":"devarv","Password":"p;a=ss"},"GitHub":{"Token":"desde-local"}}""");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private IConfigurationRoot BuildFromFiles(string environmentName) => EnvironmentTools.Build(_dir, environmentName);

    [Fact]
    public void LayersAppSettingsByEnvironmentAndLocalFile()
    {
        var configuration = BuildFromFiles("Development");

        Assert.Equal("dev", configuration["App:Name"]);
        Assert.Equal("desde-local", configuration["GitHub:Token"]);
        Assert.Equal("base", BuildFromFiles("Production")["App:Name"]);
    }

    [Fact]
    public void MissingSectionThrows()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();

        var ex = Assert.Throws<ArgumentException>(() => EnvironmentTools.GetSectionData(configuration, EnvironmentTools.SectionName));

        Assert.Contains("EnvironmentData", ex.Message);
    }

    [Fact]
    public void ProcessEnvironmentVariablesAreNotRead()
    {
        var key = $"DevArvProbe{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable($"{key}__Valor", "desde-entorno");
        Environment.SetEnvironmentVariable("GitHub__Token", "desde-entorno");
        try
        {
            var configuration = BuildFromFiles("Development");

            Assert.Null(configuration[$"{key}:Valor"]);
            Assert.Equal("desde-local", configuration["GitHub:Token"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable($"{key}__Valor", null);
            Environment.SetEnvironmentVariable("GitHub__Token", null);
        }
    }

    [Fact]
    public void HostSourcesAreReplacedByTheAppSettings()
    {
        // Mismo orden de fuentes que arma el host por defecto.
        File.WriteAllText(Path.Combine(_dir, "secrets.json"), """{"GitHub":{"Token":"desde-user-secrets"},"Solo":{"Secreto":"x"}}""");
        var host = new ConfigurationManager();
        host.SetBasePath(_dir);
        host.AddEnvironmentVariables(prefix: "DOTNET_");
        host.AddJsonFile("appsettings.json", optional: true);
        host.AddJsonFile("secrets.json", optional: true);
        host.AddEnvironmentVariables();
        host.AddCommandLine(["--GitHub:Token=desde-cli", "--Solo:Cli=x"]);

        EnvironmentTools.ReplaceSources(host, BuildFromFiles("Development"));

        Assert.Equal("desde-local", host["GitHub:Token"]);
        Assert.Equal("dev", host["App:Name"]);
        Assert.Null(host["Solo:Secreto"]);
        Assert.Null(host["Solo:Cli"]);
        var sources = ((IConfigurationBuilder)host).Sources;
        Assert.DoesNotContain(sources, s => s is CommandLineConfigurationSource or JsonConfigurationSource);
        Assert.Single(sources.OfType<EnvironmentVariablesConfigurationSource>(), s => s.Prefix == "DOTNET_");
        Assert.DoesNotContain(sources.OfType<EnvironmentVariablesConfigurationSource>(), s => string.IsNullOrEmpty(s.Prefix));
    }

    [Fact]
    public void ReadsDatabaseDataAndBuildsAnEscapedConnectionString()
    {
        var data = EnvironmentTools.GetSectionData(BuildFromFiles("Development"), EnvironmentTools.SectionName);

        Assert.Equal("db.local", data.Host);
        var parsed = new Npgsql.NpgsqlConnectionStringBuilder(data.GetConnectionString());
        Assert.Equal("db.local", parsed.Host);
        Assert.Equal(5432, parsed.Port);
        Assert.Equal("devarv", parsed.Database);
        Assert.Equal("p;a=ss", parsed.Password);
    }

    [Theory]
    [InlineData("Host", "", "EnvironmentData:Host")]
    [InlineData("Port", "99999", "EnvironmentData:Port")]
    [InlineData("Port", "abc", "EnvironmentData:Port")]
    [InlineData("Database", " ", "EnvironmentData:Database")]
    [InlineData("Username", "", "EnvironmentData:Username")]
    [InlineData("Password", "", "EnvironmentData:Password")]
    public void InvalidDatabaseDataThrowsWithTheKeyName(string key, string value, string expected)
    {
        var values = new Dictionary<string, string?>
        {
            ["EnvironmentData:Host"] = "h", ["EnvironmentData:Port"] = "5432", ["EnvironmentData:Database"] = "d",
            ["EnvironmentData:Username"] = "u", ["EnvironmentData:Password"] = "p",
        };
        values[$"EnvironmentData:{key}"] = value;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var ex = Assert.Throws<ArgumentException>(() => EnvironmentTools.GetSectionData(configuration, EnvironmentTools.SectionName));

        Assert.StartsWith(expected, ex.Message);
    }
}
