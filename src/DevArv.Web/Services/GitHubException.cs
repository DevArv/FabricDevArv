using System.Net;

namespace DevArv.Web.Services;

public sealed class GitHubException(HttpStatusCode status, string message) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
}
