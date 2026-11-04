using DevArv.Worker.Helpers;
using DevArv.Worker.Models;

namespace DevArv.Tests.Worker.Helpers;

public class CredentialSourcesTests
{
    private static Func<string, string?> Env(params (string Key, string Value)[] vars) =>
        key => vars.FirstOrDefault(v => v.Key == key).Value;

    [Fact]
    public void ConfiguredApiKeyWinsOverEverything()
    {
        var options = new ClaudeOptions
        {
            AnthropicApiKey = "sk-ant-api03-x",
            Environment = { [CredentialSources.OAuthTokenVariable] = "sk-ant-oat01-x" },
        };

        var source = CredentialSources.ForClaude(options, Env((CredentialSources.ApiKeyVariable, "otra")));

        Assert.True(source.Configured);
        Assert.Contains("Worker:Claude:AnthropicApiKey", source.Description);
    }

    [Fact]
    public void EnvironmentSectionApiKeyIsUsedWhenOptionIsEmpty()
    {
        var options = new ClaudeOptions { AnthropicApiKey = "  ", Environment = { [CredentialSources.ApiKeyVariable] = "k" } };

        var source = CredentialSources.ForClaude(options, Env());

        Assert.True(source.Configured);
        Assert.Contains("Worker:Claude:Environment", source.Description);
        Assert.Contains(CredentialSources.ApiKeyVariable, source.Description);
    }

    [Fact]
    public void ProcessApiKeyIsPreferredOverOAuthToken()
    {
        // Claude Code usa ANTHROPIC_API_KEY antes que el token de suscripción.
        var options = new ClaudeOptions { Environment = { [CredentialSources.OAuthTokenVariable] = "sk-ant-oat01-x" } };

        var source = CredentialSources.ForClaude(options, Env((CredentialSources.ApiKeyVariable, "k")));

        Assert.True(source.Configured);
        Assert.Equal($"variable de entorno {CredentialSources.ApiKeyVariable}", source.Description);
    }

    [Fact]
    public void OAuthTokenFromEnvironmentSection()
    {
        var options = new ClaudeOptions { Environment = { [CredentialSources.OAuthTokenVariable] = "sk-ant-oat01-x" } };

        var source = CredentialSources.ForClaude(options, Env());

        Assert.True(source.Configured);
        Assert.Contains(CredentialSources.OAuthTokenVariable, source.Description);
    }

    [Fact]
    public void OAuthTokenFromProcessEnvironment()
    {
        var source = CredentialSources.ForClaude(new ClaudeOptions(), Env((CredentialSources.OAuthTokenVariable, "t")));

        Assert.True(source.Configured);
        Assert.Equal($"variable de entorno {CredentialSources.OAuthTokenVariable}", source.Description);
    }

    [Fact]
    public void FallsBackToClaudeLoginSession()
    {
        var source = CredentialSources.ForClaude(new ClaudeOptions(), Env());

        Assert.False(source.Configured);
        Assert.Contains("sesión", source.Description);
    }

    [Theory]
    [InlineData("github_pat_x", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void GitHubTokenIsDetected(string? token, bool configured)
    {
        var source = CredentialSources.ForGitHub(new GitOptions { GitHubToken = token });

        Assert.Equal(configured, source.Configured);
    }
}
