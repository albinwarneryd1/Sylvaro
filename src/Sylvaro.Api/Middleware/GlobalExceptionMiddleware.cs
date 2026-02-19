using System.Net.Mime;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Normyx.Api.Contracts.Errors;

namespace Normyx.Api.Middleware;

public sealed class GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            var (statusCode, code, message) = MapException(ex);
            var correlationId = context.Items.TryGetValue(CorrelationIdMiddleware.HttpContextItemKey, out var value)
                ? value?.ToString() ?? context.TraceIdentifier
                : context.TraceIdentifier;

            logger.LogError(
                ex,
                "Unhandled exception for {Method} {Path}. CorrelationId={CorrelationId}",
                context.Request.Method,
                context.Request.Path,
                correlationId);

            if (context.Response.HasStarted)
            {
                throw;
            }

            context.Response.Clear();
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = MediaTypeNames.Application.Json;

            var payload = new ApiErrorEnvelope(
                correlationId,
                new ApiErrorDetail(code, message));

            await context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOptions));
        }
    }

    private static (int StatusCode, string Code, string Message) MapException(Exception ex) => ex switch
    {
        UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "unauthorized", "Authentication is required."),
        ArgumentException arg => (StatusCodes.Status400BadRequest, "bad_request", arg.Message),
        InvalidOperationException invalid => (StatusCodes.Status400BadRequest, "invalid_operation", invalid.Message),
        DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "concurrency_conflict", "The resource was modified by another operation."),
        DbUpdateException => (StatusCodes.Status409Conflict, "data_update_conflict", "The update could not be completed."),
        BadHttpRequestException bad => (StatusCodes.Status400BadRequest, "bad_http_request", bad.Message),
        _ => (StatusCodes.Status500InternalServerError, "internal_server_error", "An unexpected server error occurred.")
    };
}
