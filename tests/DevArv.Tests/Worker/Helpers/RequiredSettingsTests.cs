using DevArv.Worker.Helpers;
using DevArv.Worker.Models;

namespace DevArv.Tests.Worker.Helpers;

public class RequiredSettingsTests
{
    private static WorkerOptions Complete() => new()
    {
        Server = new ServerOptions { Url = "https://devarv.local", ApiKey = "dvw_x" },
        Claude = new ClaudeOptions { AnthropicApiKey = "sk-ant-api03-x" },
        Git = new GitOptions { GitHubToken = "github_pat_x" },
    };

    [Fact]
    public void CompleteSettingsHaveNothingMissing()
    {
        Assert.Empty(RequiredSettings.Missing(Complete()));
    }

    [Fact]
    public void EmptyOptionsReportEveryRequiredKey()
    {
        var missing = RequiredSettings.Missing(new WorkerOptions());

        Assert.Equal(
            new[] { "Worker:Server:Url", "Worker:Server:ApiKey", "Worker:Claude:AnthropicApiKey", "Worker:Git:GitHubToken" },
            missing);
    }

    [Fact]
    public void BlankCredentialsCountAsMissing()
    {
        var options = Complete();
        options.Claude.AnthropicApiKey = "   ";
        options.Git.GitHubToken = null;

        var missing = RequiredSettings.Missing(options);

        Assert.Equal(new[] { "Worker:Claude:AnthropicApiKey", "Worker:Git:GitHubToken" }, missing);
    }

    [Fact]
    public void CredentialsInClaudeEnvironmentDoNotReplaceTheApiKey()
    {
        // La API key sólo se acepta en Worker:Claude:AnthropicApiKey.
        var options = Complete();
        options.Claude.AnthropicApiKey = "";
        options.Claude.Environment["ANTHROPIC_API_KEY"] = "sk-ant-api03-x";

        Assert.Contains("Worker:Claude:AnthropicApiKey", RequiredSettings.Missing(options));
    }
}
