using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Normyx.Api.Utilities;
using Normyx.Application.Abstractions;
using Normyx.Infrastructure.Persistence;

namespace Normyx.Api.Endpoints;

public static class AuditEndpoints
{
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/audit").WithTags("Audit").RequireAuthorization();

        group.MapGet("", ListAuditLogsAsync);

        return app;
    }

    private static async Task<IResult> ListAuditLogsAsync(
        [FromQuery] int take,
        NormyxDbContext dbContext,
        ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);
        var limit = take <= 0 || take > 500 ? 100 : take;

        var logs = await dbContext.AuditLogs
            .Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.Timestamp)
            .Take(limit)
            .Select(x => new
            {
                x.Id,
                x.ActorUserId,
                x.ActionType,
                x.TargetType,
                x.TargetId,
                x.Timestamp,
                x.BeforeJson,
                x.AfterJson,
                x.Ip,
                x.UserAgent
            })
            .ToListAsync();

        return Results.Ok(logs);
    }
}
