using Microsoft.AspNetCore.Http;
using Normyx.Infrastructure.Persistence;
using Normyx.Domain.Entities;
using System.Security.Claims;

namespace Normyx.Infrastructure.Audit;

public class AuditMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, NormyxDbContext dbContext)
    {
        await next(context);

        if (context.Request.Path.StartsWithSegments("/health"))
        {
            return;
        }

        var tenantId = ParseGuid(context.User.FindFirstValue("tenant_id")) ?? Guid.Empty;
        var actorId = ParseGuid(context.User.FindFirstValue(ClaimTypes.NameIdentifier));

        var audit = new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ActorUserId = actorId,
            ActionType = $"HTTP_{context.Request.Method}",
            TargetType = "Endpoint",
            TargetId = null,
            BeforeJson = "{}",
            AfterJson = $"{{\"path\":\"{context.Request.Path}\",\"status\":{context.Response.StatusCode}}}",
            Ip = context.Connection.RemoteIpAddress?.ToString() ?? "",
            UserAgent = context.Request.Headers.UserAgent.ToString(),
            Timestamp = DateTimeOffset.UtcNow
        };

        dbContext.AuditLogs.Add(audit);
        await dbContext.SaveChangesAsync();
    }

    private static Guid? ParseGuid(string? value)
        => Guid.TryParse(value, out var guid) ? guid : null;
}
