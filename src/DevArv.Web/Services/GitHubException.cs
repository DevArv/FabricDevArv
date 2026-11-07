using System.Net;

namespace DevArv.Web.Services;

public sealed class GitHubException(HttpStatusCode StatusCode, string Message) : Exception(Message)
{
    public HttpStatusCode Status { get; } = StatusCode;
}
