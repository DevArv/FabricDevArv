using DevArv.Contracts.Models;
using DevArv.Web.DataModels;
using DevArv.Web.Helpers;
using DevArv.Web.Repositories;

namespace DevArv.Web.Api;

/// <summary>Autentica al worker con la cabecera X-DevArv-Key y lo deja en HttpContext.Items.</summary>
public sealed class WorkerAuthFilter : IEndpointFilter
{
    public const string ITEM_KEY = "devarv.worker";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext Context, EndpointFilterDelegate Next)
    {
        var http = Context.HttpContext;
        var key = http.Request.Headers[WorkerHeaders.API_KEY].ToString();
        if (string.IsNullOrWhiteSpace(key)) return Results.Unauthorized();

        var workers = http.RequestServices.GetRequiredService<WorkersRepository>();
        var worker = await workers.AuthenticateAsync(
            KeyHash: ApiKeys.Hash(key),
            CT: http.RequestAborted);
        if (worker is null) return Results.Unauthorized();

        http.Items[ITEM_KEY] = worker;
        return await Next(Context);
    }

    public static WorkerNode Current(HttpContext Http) => (WorkerNode)Http.Items[ITEM_KEY]!;
}
