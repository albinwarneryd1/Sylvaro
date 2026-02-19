using System.Text;
using System.Text.Json;
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
        var group = app.MapGroup("/audit").WithTags("Audit").RequireAuthorization().WithRequestValidation();

        group.MapGet("", ListAuditLogsAsync);
        group.MapGet("/export", ExportAuditLogsAsync);

        return app;
    }

    private static IQueryable<Normyx.Domain.Entities.AuditLog> ApplyFilters(
        IQueryable<Normyx.Domain.Entities.AuditLog> query,
        Guid tenantId,
        Guid? actorUserId,
        string? actionType,
        string? targetType,
        Guid? targetId)
    {
        query = query.Where(x => x.TenantId == tenantId);

        if (actorUserId is not null)
        {
            query = query.Where(x => x.ActorUserId == actorUserId.Value);
        }

        if (!string.IsNullOrWhiteSpace(actionType))
        {
            query = query.Where(x => x.ActionType == actionType);
        }

        if (!string.IsNullOrWhiteSpace(targetType))
        {
            query = query.Where(x => x.TargetType == targetType);
        }

        if (targetId is not null)
        {
            query = query.Where(x => x.TargetId == targetId.Value);
        }

        return query;
    }

    private static async Task<IResult> ListAuditLogsAsync(
        [FromQuery] int take,
        [FromQuery] Guid? actorUserId,
        [FromQuery] string? actionType,
        [FromQuery] string? targetType,
        [FromQuery] Guid? targetId,
        NormyxDbContext dbContext,
        ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);
        var limit = take <= 0 || take > 500 ? 100 : take;

        var logs = await ApplyFilters(
                dbContext.AuditLogs.AsNoTracking(),
                tenantId,
                actorUserId,
                actionType,
                targetType,
                targetId)
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

    private static async Task<IResult> ExportAuditLogsAsync(
        [FromQuery] int take,
        [FromQuery] Guid? actorUserId,
        [FromQuery] string? actionType,
        [FromQuery] string? targetType,
        [FromQuery] Guid? targetId,
        NormyxDbContext dbContext,
        ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);
        var limit = take <= 0 || take > 5000 ? 1500 : take;

        var logs = await ApplyFilters(
                dbContext.AuditLogs.AsNoTracking(),
                tenantId,
                actorUserId,
                actionType,
                targetType,
                targetId)
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

        var payload = JsonSerializer.Serialize(logs, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        var fileName = $"audit-export-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.json";
        return Results.File(Encoding.UTF8.GetBytes(payload), "application/json", fileName);
    }
}
