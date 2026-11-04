using DevArv.Contracts.Models;
using DevArv.Web.DataModels;
using DevArv.Web.DatabaseConnection;
using DevArv.Web.Helpers;
using Microsoft.EntityFrameworkCore;

namespace DevArv.Web.Api;

/// <summary>Autentica al worker con la cabecera X-DevArv-Key y lo deja en HttpContext.Items.</summary>
public sealed class WorkerAuthFilter : IEndpointFilter
{
    public const string ItemKey = "devarv.worker";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var key = http.Request.Headers[WorkerHeaders.ApiKey].ToString();
        if (string.IsNullOrWhiteSpace(key)) return Results.Unauthorized();

        var db = http.RequestServices.GetRequiredService<AppDbContext>();
        var hash = ApiKeys.Hash(key);
        var worker = await db.Workers.FirstOrDefaultAsync(w => w.KeyHash == hash && w.IsEnabled, http.RequestAborted);
        if (worker is null) return Results.Unauthorized();

        worker.LastSeenAt = DateTimeOffset.UtcNow;
        http.Items[ItemKey] = worker;
        return await next(context);
    }

    public static WorkerNode Current(HttpContext http) => (WorkerNode)http.Items[ItemKey]!;
}
