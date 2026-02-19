using System.Net.Mime;
using System.Security.Claims;
using System.Text.Json;
using Normyx.Api.Contracts.Errors;

namespace Normyx.Api.Middleware;

public sealed class TenantIsolationMiddleware(RequestDelegate next)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task InvokeAsync(HttpContext context)
    {
        if (!RequiresTenantIsolation(context))
        {
            await next(context);
            return;
        }

        var tenantClaim = context.User.FindFirstValue("tenant_id");
        if (!Guid.TryParse(tenantClaim, out _))
        {
            await WriteUnauthorizedAsync(context, "Tenant claim is missing or invalid.");
            return;
        }

        await next(context);
    }

    private static bool RequiresTenantIsolation(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/health") ||
            context.Request.Path.StartsWithSegments("/auth") ||
            context.Request.Path.StartsWithSegments("/swagger") ||
            context.Request.Path.StartsWithSegments("/openapi"))
        {
            return false;
        }

        return context.User.Identity?.IsAuthenticated == true;
    }

    private static async Task WriteUnauthorizedAsync(HttpContext context, string message)
    {
        var correlationId = context.Items.TryGetValue(CorrelationIdMiddleware.HttpContextItemKey, out var value)
            ? value?.ToString() ?? context.TraceIdentifier
            : context.TraceIdentifier;

        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = MediaTypeNames.Application.Json;
        var payload = new ApiErrorEnvelope(correlationId, new ApiErrorDetail("tenant_claim_missing", message));
        await context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOptions));
    }
}
