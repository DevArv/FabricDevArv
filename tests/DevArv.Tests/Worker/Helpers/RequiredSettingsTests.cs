using DevArv.Worker.Helpers;
using DevArv.Worker.Models;

namespace DevArv.Tests.Worker.Helpers;

public class RequiredSettingsTests
{
    private static WorkerOptions Complete() => new()
    {
        Claude = new ClaudeOptions { AnthropicApiKey = "sk-ant-api03-x" },
        Git = new GitOptions { GitHubToken = "github_pat_x" },
    };

    [Fact]
    public void CompleteSettingsHaveNothingMissing()
    {
        Assert.Empty(RequiredSettings.Missing(Complete()));
        RequiredSettings.EnsureComplete(Complete());
    }

    [Fact]
    public void EmptyOptionsReportEveryRequiredKey()
    {
        var missing = RequiredSettings.Missing(new WorkerOptions());

        Assert.Equal(2, missing.Count);
        Assert.Contains("Worker:Claude:OAuthToken", missing[0]);
        Assert.Contains("Worker:Claude:AnthropicApiKey", missing[0]);
        Assert.Equal("Worker:Git:GitHubToken", missing[1]);
    }

    [Fact]
    public void BlankCredentialsCountAsMissing()
    {
        var options = Complete();
        options.Claude.AnthropicApiKey = "   ";

        var ex = Assert.Throws<ArgumentException>(() => RequiredSettings.EnsureComplete(options));

        Assert.Contains("Worker:Claude:AnthropicApiKey", ex.Message);
        Assert.DoesNotContain("Worker:Git:GitHubToken", ex.Message);
    }

    [Fact]
    public void SubscriptionTokenAloneIsEnough()
    {
        var options = Complete();
        options.Claude.AnthropicApiKey = null;
        options.Claude.OAuthToken = "sk-ant-oat01-x";

        Assert.Empty(RequiredSettings.Missing(options));
        RequiredSettings.EnsureComplete(options);
    }

    [Fact]
    public void BothClaudeCredentialsAreRejected()
    {
        // Con las dos, Claude Code usaría la API key y se cobraría por uso.
        var options = Complete();
        options.Claude.OAuthToken = "sk-ant-oat01-x";

        var ex = Assert.Throws<ArgumentException>(() => RequiredSettings.EnsureComplete(options));

        Assert.Contains("no ambas", ex.Message);
    }

    [Fact]
    public void CredentialsInClaudeEnvironmentDoNotReplaceTheApiKey()
    {
        // La API key sólo se acepta en Worker:Claude:AnthropicApiKey.
        var options = Complete();
        options.Claude.AnthropicApiKey = "";
        options.Claude.Environment["ANTHROPIC_API_KEY"] = "sk-ant-api03-x";

        Assert.Contains("Worker:Claude:AnthropicApiKey", RequiredSettings.Missing(options)[0]);
    }
}
