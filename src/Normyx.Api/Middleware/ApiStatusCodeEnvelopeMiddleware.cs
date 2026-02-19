using System.Net.Mime;
using System.Text.Json;
using Normyx.Api.Contracts.Errors;

namespace Normyx.Api.Middleware;

public sealed class ApiStatusCodeEnvelopeMiddleware(RequestDelegate next)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task InvokeAsync(HttpContext context)
    {
        await next(context);

        if (context.Response.HasStarted ||
            context.Request.Path.StartsWithSegments("/health") ||
            context.Response.StatusCode < 400 ||
            context.Response.ContentLength.GetValueOrDefault() > 0 ||
            !string.IsNullOrWhiteSpace(context.Response.ContentType))
        {
            return;
        }

        var correlationId = context.Items.TryGetValue(CorrelationIdMiddleware.HttpContextItemKey, out var value)
            ? value?.ToString() ?? context.TraceIdentifier
            : context.TraceIdentifier;

        var (code, message) = MapStatusCode(context.Response.StatusCode);
        var payload = new ApiErrorEnvelope(correlationId, new ApiErrorDetail(code, message));

        context.Response.ContentType = MediaTypeNames.Application.Json;
        await context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOptions));
    }

    private static (string Code, string Message) MapStatusCode(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => ("bad_request", "Request validation failed."),
        StatusCodes.Status401Unauthorized => ("unauthorized", "Authentication is required."),
        StatusCodes.Status403Forbidden => ("forbidden", "Insufficient permissions."),
        StatusCodes.Status404NotFound => ("not_found", "The requested resource was not found."),
        StatusCodes.Status405MethodNotAllowed => ("method_not_allowed", "HTTP method is not allowed on this endpoint."),
        StatusCodes.Status409Conflict => ("conflict", "The request could not be completed due to a conflict."),
        StatusCodes.Status422UnprocessableEntity => ("unprocessable_entity", "The server could not process the payload."),
        StatusCodes.Status429TooManyRequests => ("rate_limited", "Too many requests. Please retry later."),
        _ => ("request_failed", "The request failed.")
    };
}
