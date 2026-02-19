using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Normyx.Infrastructure.Persistence;
using Normyx.Domain.Entities;
using System.Security.Claims;

namespace Normyx.Infrastructure.Audit;

public class AuditMiddleware(RequestDelegate next, ILogger<AuditMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, NormyxDbContext dbContext)
    {
        await next(context);

        if (context.Request.Path.StartsWithSegments("/health") ||
            context.Request.Path.StartsWithSegments("/swagger") ||
            context.Request.Path.StartsWithSegments("/openapi"))
        {
            return;
        }

        try
        {
            var tenantId = ParseGuid(context.User.FindFirstValue("tenant_id")) ?? Guid.Empty;
            var actorId = ParseGuid(context.User.FindFirstValue(ClaimTypes.NameIdentifier));
            var correlationId = context.Items.TryGetValue("CorrelationId", out var value)
                ? value?.ToString() ?? context.TraceIdentifier
                : context.TraceIdentifier;

            var audit = new AuditLog
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ActorUserId = actorId,
                ActionType = $"HTTP_{context.Request.Method}",
                TargetType = "Endpoint",
                TargetId = null,
                BeforeJson = "{}",
                AfterJson = $"{{\"path\":\"{context.Request.Path}\",\"status\":{context.Response.StatusCode},\"correlationId\":\"{correlationId}\"}}",
                Ip = context.Connection.RemoteIpAddress?.ToString() ?? "",
                UserAgent = context.Request.Headers.UserAgent.ToString(),
                Timestamp = DateTimeOffset.UtcNow
            };

            dbContext.AuditLogs.Add(audit);
            await dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to persist audit event for {Method} {Path}", context.Request.Method, context.Request.Path);
        }
    }

    private static Guid? ParseGuid(string? value)
        => Guid.TryParse(value, out var guid) ? guid : null;
}
