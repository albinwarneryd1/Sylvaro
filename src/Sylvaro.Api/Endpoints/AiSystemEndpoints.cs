using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Normyx.Api.Utilities;
using Normyx.Application.Abstractions;
using Normyx.Application.Security;
using Normyx.Domain.Entities;
using Normyx.Domain.Enums;
using Normyx.Infrastructure.Persistence;

namespace Normyx.Api.Endpoints;

public static class AiSystemEndpoints
{
    public static IEndpointRouteBuilder MapAiSystemEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/aisystems").WithTags("AI Systems").RequireAuthorization().WithRequestValidation();
        var writeRoles = $"{RoleNames.Admin},{RoleNames.ComplianceOfficer},{RoleNames.SecurityLead},{RoleNames.ProductOwner}";

        group.MapGet("", ListSystemsAsync);
        group.MapPost("", CreateSystemAsync).RequireAuthorization(new AuthorizeAttribute { Roles = writeRoles });
        group.MapGet("/{systemId:guid}", GetSystemAsync);
        group.MapPut("/{systemId:guid}", UpdateSystemAsync).RequireAuthorization(new AuthorizeAttribute { Roles = writeRoles });
        group.MapDelete("/{systemId:guid}", ArchiveSystemAsync).RequireAuthorization(new AuthorizeAttribute { Roles = writeRoles });

        group.MapGet("/{systemId:guid}/versions", ListVersionsAsync);
        group.MapPost("/{systemId:guid}/versions", CreateVersionAsync).RequireAuthorization(new AuthorizeAttribute { Roles = writeRoles });

        return app;
    }

    private static async Task<IResult> ListSystemsAsync(NormyxDbContext dbContext, ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);

        var systems = await dbContext.AiSystems
            .Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.UpdatedAt)
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.Description,
                Status = x.Status.ToString(),
                x.CreatedAt,
                x.UpdatedAt,
                VersionCount = x.Versions.Count
            })
            .ToListAsync();

        return Results.Ok(systems);
    }

    private record CreateAiSystemRequest(
        [property: Required, StringLength(180, MinimumLength = 2)] string Name,
        [property: StringLength(2000)] string Description,
        Guid? OwnerUserId);

    private static async Task<IResult> CreateSystemAsync(
        [FromBody] CreateAiSystemRequest request,
        NormyxDbContext dbContext,
        ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);
        var userId = TenantContext.RequireUserId(currentUser);

        var system = new AiSystem
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = request.Name,
            Description = request.Description,
            OwnerUserId = request.OwnerUserId ?? userId,
            Status = AiSystemStatus.Draft,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var initialVersion = new AiSystemVersion
        {
            Id = Guid.NewGuid(),
            AiSystemId = system.Id,
            VersionNumber = 1,
            ChangeSummary = "Initial version",
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = userId
        };

        dbContext.AiSystems.Add(system);
        dbContext.AiSystemVersions.Add(initialVersion);
        await dbContext.SaveChangesAsync();

        return Results.Created($"/aisystems/{system.Id}", new { AiSystemId = system.Id, VersionId = initialVersion.Id, initialVersion.VersionNumber });
    }

    private static async Task<IResult> GetSystemAsync([FromRoute] Guid systemId, NormyxDbContext dbContext, ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);

        var system = await dbContext.AiSystems
            .Where(x => x.Id == systemId && x.TenantId == tenantId)
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.Description,
                Status = x.Status.ToString(),
                x.OwnerUserId,
                x.CreatedAt,
                x.UpdatedAt,
                LatestVersion = x.Versions.OrderByDescending(v => v.VersionNumber).Select(v => new { v.Id, v.VersionNumber, v.ChangeSummary, v.CreatedAt }).FirstOrDefault()
            })
            .FirstOrDefaultAsync();

        return system is null ? Results.NotFound() : Results.Ok(system);
    }

    private record UpdateAiSystemRequest(
        [property: Required, StringLength(180, MinimumLength = 2)] string Name,
        [property: StringLength(2000)] string Description,
        AiSystemStatus Status,
        Guid OwnerUserId);

    private static async Task<IResult> UpdateSystemAsync(
        [FromRoute] Guid systemId,
        [FromBody] UpdateAiSystemRequest request,
        NormyxDbContext dbContext,
        ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);

        var system = await dbContext.AiSystems.FirstOrDefaultAsync(x => x.Id == systemId && x.TenantId == tenantId);
        if (system is null)
        {
            return Results.NotFound();
        }

        system.Name = request.Name;
        system.Description = request.Description;
        system.Status = request.Status;
        system.OwnerUserId = request.OwnerUserId;
        system.UpdatedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> ArchiveSystemAsync([FromRoute] Guid systemId, NormyxDbContext dbContext, ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);

        var system = await dbContext.AiSystems.FirstOrDefaultAsync(x => x.Id == systemId && x.TenantId == tenantId);
        if (system is null)
        {
            return Results.NotFound();
        }

        system.Status = AiSystemStatus.Archived;
        system.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync();

        return Results.NoContent();
    }

    private static async Task<IResult> ListVersionsAsync([FromRoute] Guid systemId, NormyxDbContext dbContext, ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);

        var versions = await dbContext.AiSystemVersions
            .Where(x => x.AiSystemId == systemId && x.AiSystem.TenantId == tenantId)
            .OrderByDescending(x => x.VersionNumber)
            .Select(x => new { x.Id, x.VersionNumber, x.ChangeSummary, x.CreatedAt, x.CreatedByUserId })
            .ToListAsync();

        return Results.Ok(versions);
    }

    private record CreateVersionRequest(
        [property: Required, StringLength(500, MinimumLength = 2)] string ChangeSummary);

    private static async Task<IResult> CreateVersionAsync(
        [FromRoute] Guid systemId,
        [FromBody] CreateVersionRequest request,
        NormyxDbContext dbContext,
        ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);
        var userId = TenantContext.RequireUserId(currentUser);

        var system = await dbContext.AiSystems
            .Include(x => x.Versions)
            .FirstOrDefaultAsync(x => x.Id == systemId && x.TenantId == tenantId);

        if (system is null)
        {
            return Results.NotFound();
        }

        var nextVersionNo = system.Versions.Count == 0 ? 1 : system.Versions.Max(x => x.VersionNumber) + 1;

        var version = new AiSystemVersion
        {
            Id = Guid.NewGuid(),
            AiSystemId = systemId,
            VersionNumber = nextVersionNo,
            ChangeSummary = request.ChangeSummary,
            CreatedByUserId = userId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        dbContext.AiSystemVersions.Add(version);
        system.UpdatedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync();
        return Results.Created($"/aisystems/{systemId}/versions/{version.Id}", new { version.Id, version.VersionNumber });
    }
}
