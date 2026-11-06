using System.Net;
using System.Text;

namespace DevArv.Tests.TestSupport;

/// <summary>Una petición HTTP ya capturada: método, ruta con query y cuerpo (texto).</summary>
public sealed record CapturedRequest(HttpMethod Method, string PathAndQuery, string Body);

/// <summary>Servidor HTTP simulado: registra cada petición y responde con lo que decida <paramref name="Respond"/>.</summary>
public sealed class StubHttpHandler(Func<CapturedRequest, HttpResponseMessage> Respond) : HttpMessageHandler
{
    public List<CapturedRequest> Requests { get; } = [];

    public static HttpResponseMessage Json(HttpStatusCode Status, string Body) =>
        new(Status) { Content = new StringContent(Body, Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage Request, CancellationToken CT)
    {
        var body = Request.Content is null ? "" : await Request.Content.ReadAsStringAsync(CT);
        var captured = new CapturedRequest(Request.Method, Request.RequestUri!.PathAndQuery, body);
        Requests.Add(captured);
        return Respond(captured);
    }
}
