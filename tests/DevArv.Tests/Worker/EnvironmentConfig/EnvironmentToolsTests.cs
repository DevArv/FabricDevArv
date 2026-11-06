using DevArv.Worker.EnvironmentConfig;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.CommandLine;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;

namespace DevArv.Tests.Worker.EnvironmentConfig;

public sealed class EnvironmentToolsTests : IDisposable
{
    private readonly string _Dir = Directory.CreateTempSubdirectory("devarv-env-").FullName;

    public EnvironmentToolsTests()
    {
        File.WriteAllText(Path.Combine(_Dir, "appsettings.json"), """{"EnvironmentData":{"Url":"https://localhost:5001","ApiKey":""},"GitHub":{"Token":""},"App":{"Name":"base"}}""");
        File.WriteAllText(Path.Combine(_Dir, "appsettings.Development.json"), """{"App":{"Name":"dev"}}""");
        File.WriteAllText(Path.Combine(_Dir, EnvironmentTools.LOCAL_FILE), """{"EnvironmentData":{"Url":"https://devarv.local/","ApiKey":" dvw_x "},"GitHub":{"Token":"desde-local"}}""");
    }

    public void Dispose() => Directory.Delete(_Dir, recursive: true);

    private IConfigurationRoot BuildFromFiles(string EnvironmentName) => EnvironmentTools.Build(_Dir, EnvironmentName);

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

        var ex = Assert.Throws<ArgumentException>(() => EnvironmentTools.GetSectionData(configuration, EnvironmentTools.SECTION_NAME));

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
        File.WriteAllText(Path.Combine(_Dir, "secrets.json"), """{"GitHub":{"Token":"desde-user-secrets"},"Solo":{"Secreto":"x"}}""");
        var host = new ConfigurationManager();
        host.SetBasePath(_Dir);
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
    public void ReadsServerDataFromTheLocalFile()
    {
        var data = EnvironmentTools.GetSectionData(BuildFromFiles("Development"), EnvironmentTools.SECTION_NAME);

        Assert.Equal("https://devarv.local/", data.Url);
        Assert.Equal("dvw_x", data.ApiKey);
        Assert.Equal(new Uri("https://devarv.local/"), data.GetBaseAddress());
    }

    [Theory]
    [InlineData("", "dvw_x", "EnvironmentData:Url")]
    [InlineData("devarv.local", "dvw_x", "EnvironmentData:Url")]
    [InlineData("ftp://devarv.local", "dvw_x", "EnvironmentData:Url")]
    [InlineData("https://devarv.local", " ", "EnvironmentData:ApiKey")]
    public void InvalidServerDataThrowsWithTheKeyName(string Url, string ApiKey, string Expected)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["EnvironmentData:Url"] = Url, ["EnvironmentData:ApiKey"] = ApiKey,
        }).Build();

        var ex = Assert.Throws<ArgumentException>(() => EnvironmentTools.GetSectionData(configuration, EnvironmentTools.SECTION_NAME));

        Assert.StartsWith(Expected, ex.Message);
    }
}
