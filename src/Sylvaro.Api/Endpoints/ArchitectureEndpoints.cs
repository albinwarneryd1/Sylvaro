using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Normyx.Api.Utilities;
using Normyx.Application.Abstractions;
using Normyx.Application.Security;
using Normyx.Domain.Entities;
using Normyx.Infrastructure.Persistence;

namespace Normyx.Api.Endpoints;

public static class ArchitectureEndpoints
{
    public static IEndpointRouteBuilder MapArchitectureEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/versions/{versionId:guid}/architecture").WithTags("Architecture").RequireAuthorization().WithRequestValidation();
        var writeRoles = $"{RoleNames.Admin},{RoleNames.ComplianceOfficer},{RoleNames.SecurityLead},{RoleNames.ProductOwner}";

        group.MapGet("", GetArchitectureAsync);
        group.MapPost("/components", AddComponentAsync).RequireAuthorization(new AuthorizeAttribute { Roles = writeRoles });
        group.MapPut("/components/{componentId:guid}", UpdateComponentAsync).RequireAuthorization(new AuthorizeAttribute { Roles = writeRoles });
        group.MapDelete("/components/{componentId:guid}", DeleteComponentAsync).RequireAuthorization(new AuthorizeAttribute { Roles = writeRoles });

        group.MapPost("/flows", AddFlowAsync).RequireAuthorization(new AuthorizeAttribute { Roles = writeRoles });
        group.MapPut("/flows/{flowId:guid}", UpdateFlowAsync).RequireAuthorization(new AuthorizeAttribute { Roles = writeRoles });
        group.MapDelete("/flows/{flowId:guid}", DeleteFlowAsync).RequireAuthorization(new AuthorizeAttribute { Roles = writeRoles });

        group.MapPost("/stores", AddStoreAsync).RequireAuthorization(new AuthorizeAttribute { Roles = writeRoles });
        group.MapPut("/stores/{storeId:guid}", UpdateStoreAsync).RequireAuthorization(new AuthorizeAttribute { Roles = writeRoles });
        group.MapDelete("/stores/{storeId:guid}", DeleteStoreAsync).RequireAuthorization(new AuthorizeAttribute { Roles = writeRoles });

        return app;
    }

    private static async Task<bool> VersionBelongsToTenantAsync(NormyxDbContext dbContext, Guid versionId, Guid tenantId)
        => await dbContext.AiSystemVersions.AnyAsync(v => v.Id == versionId && v.AiSystem.TenantId == tenantId);

    private static async Task<bool> ComponentBelongsToVersionAsync(NormyxDbContext dbContext, Guid versionId, Guid componentId)
        => await dbContext.Components.AnyAsync(c => c.Id == componentId && c.AiSystemVersionId == versionId);

    private static async Task<IResult> GetArchitectureAsync([FromRoute] Guid versionId, NormyxDbContext dbContext, ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);
        if (!await VersionBelongsToTenantAsync(dbContext, versionId, tenantId))
        {
            return Results.NotFound();
        }

        var components = await dbContext.Components.Where(x => x.AiSystemVersionId == versionId).ToListAsync();
        var flows = await dbContext.DataFlows.Where(x => x.AiSystemVersionId == versionId).ToListAsync();
        var stores = await dbContext.DataStores.Where(x => x.AiSystemVersionId == versionId).ToListAsync();

        return Results.Ok(new { components, flows, stores });
    }

    private record UpsertComponentRequest(
        [property: Required, StringLength(120, MinimumLength = 2)] string Name,
        [property: Required, StringLength(80, MinimumLength = 2)] string Type,
        [property: StringLength(2000)] string Description,
        [property: Required, StringLength(80, MinimumLength = 2)] string TrustZone,
        bool IsExternal,
        [property: Required, StringLength(40, MinimumLength = 2)] string DataSensitivityLevel);

    private static async Task<IResult> AddComponentAsync([FromRoute] Guid versionId, [FromBody] UpsertComponentRequest request, NormyxDbContext dbContext, ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);
        if (!await VersionBelongsToTenantAsync(dbContext, versionId, tenantId))
        {
            return Results.NotFound();
        }

        var component = new Component
        {
            Id = Guid.NewGuid(),
            AiSystemVersionId = versionId,
            Name = request.Name,
            Type = request.Type,
            Description = request.Description,
            TrustZone = request.TrustZone,
            IsExternal = request.IsExternal,
            DataSensitivityLevel = request.DataSensitivityLevel
        };

        dbContext.Components.Add(component);
        await dbContext.SaveChangesAsync();
        return Results.Created($"/versions/{versionId}/architecture/components/{component.Id}", component);
    }

    private static async Task<IResult> UpdateComponentAsync([FromRoute] Guid versionId, [FromRoute] Guid componentId, [FromBody] UpsertComponentRequest request, NormyxDbContext dbContext, ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);
        if (!await VersionBelongsToTenantAsync(dbContext, versionId, tenantId))
        {
            return Results.NotFound();
        }

        var component = await dbContext.Components.FirstOrDefaultAsync(x => x.Id == componentId && x.AiSystemVersionId == versionId);
        if (component is null)
        {
            return Results.NotFound();
        }

        component.Name = request.Name;
        component.Type = request.Type;
        component.Description = request.Description;
        component.TrustZone = request.TrustZone;
        component.IsExternal = request.IsExternal;
        component.DataSensitivityLevel = request.DataSensitivityLevel;

        await dbContext.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteComponentAsync([FromRoute] Guid versionId, [FromRoute] Guid componentId, NormyxDbContext dbContext, ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);
        if (!await VersionBelongsToTenantAsync(dbContext, versionId, tenantId))
        {
            return Results.NotFound();
        }

        var component = await dbContext.Components.FirstOrDefaultAsync(x => x.Id == componentId && x.AiSystemVersionId == versionId);
        if (component is null)
        {
            return Results.NotFound();
        }

        dbContext.Components.Remove(component);
        await dbContext.SaveChangesAsync();
        return Results.NoContent();
    }

    private record UpsertFlowRequest(
        Guid FromComponentId,
        Guid ToComponentId,
        [property: MinLength(1)] string[] DataCategories,
        [property: Required, StringLength(200, MinimumLength = 2)] string Purpose,
        bool EncryptionInTransit,
        [property: StringLength(1000)] string Notes);

    private static async Task<IResult> AddFlowAsync([FromRoute] Guid versionId, [FromBody] UpsertFlowRequest request, NormyxDbContext dbContext, ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);
        if (!await VersionBelongsToTenantAsync(dbContext, versionId, tenantId))
        {
            return Results.NotFound();
        }

        if (!await ComponentBelongsToVersionAsync(dbContext, versionId, request.FromComponentId) ||
            !await ComponentBelongsToVersionAsync(dbContext, versionId, request.ToComponentId))
        {
            return Results.BadRequest(new { message = "Flow endpoints must reference existing components in the version." });
        }

        var flow = new DataFlow
        {
            Id = Guid.NewGuid(),
            AiSystemVersionId = versionId,
            FromComponentId = request.FromComponentId,
            ToComponentId = request.ToComponentId,
            DataCategories = request.DataCategories,
            Purpose = request.Purpose,
            EncryptionInTransit = request.EncryptionInTransit,
            Notes = request.Notes
        };

        dbContext.DataFlows.Add(flow);
        await dbContext.SaveChangesAsync();
        return Results.Created($"/versions/{versionId}/architecture/flows/{flow.Id}", flow);
    }

    private static async Task<IResult> UpdateFlowAsync([FromRoute] Guid versionId, [FromRoute] Guid flowId, [FromBody] UpsertFlowRequest request, NormyxDbContext dbContext, ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);
        if (!await VersionBelongsToTenantAsync(dbContext, versionId, tenantId))
        {
            return Results.NotFound();
        }

        if (!await ComponentBelongsToVersionAsync(dbContext, versionId, request.FromComponentId) ||
            !await ComponentBelongsToVersionAsync(dbContext, versionId, request.ToComponentId))
        {
            return Results.BadRequest(new { message = "Flow endpoints must reference existing components in the version." });
        }

        var flow = await dbContext.DataFlows.FirstOrDefaultAsync(x => x.Id == flowId && x.AiSystemVersionId == versionId);
        if (flow is null)
        {
            return Results.NotFound();
        }

        flow.FromComponentId = request.FromComponentId;
        flow.ToComponentId = request.ToComponentId;
        flow.DataCategories = request.DataCategories;
        flow.Purpose = request.Purpose;
        flow.EncryptionInTransit = request.EncryptionInTransit;
        flow.Notes = request.Notes;

        await dbContext.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteFlowAsync([FromRoute] Guid versionId, [FromRoute] Guid flowId, NormyxDbContext dbContext, ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);
        if (!await VersionBelongsToTenantAsync(dbContext, versionId, tenantId))
        {
            return Results.NotFound();
        }

        var flow = await dbContext.DataFlows.FirstOrDefaultAsync(x => x.Id == flowId && x.AiSystemVersionId == versionId);
        if (flow is null)
        {
            return Results.NotFound();
        }

        dbContext.DataFlows.Remove(flow);
        await dbContext.SaveChangesAsync();
        return Results.NoContent();
    }

    private record UpsertStoreRequest(
        Guid ComponentId,
        [property: Required, StringLength(80, MinimumLength = 2)] string StorageType,
        [property: Required, StringLength(80, MinimumLength = 2)] string Region,
        [property: Range(1, 36500)] int RetentionDays,
        bool EncryptionAtRest,
        [property: Required, StringLength(120, MinimumLength = 2)] string AccessModel);

    private static async Task<IResult> AddStoreAsync([FromRoute] Guid versionId, [FromBody] UpsertStoreRequest request, NormyxDbContext dbContext, ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);
        if (!await VersionBelongsToTenantAsync(dbContext, versionId, tenantId))
        {
            return Results.NotFound();
        }

        if (!await ComponentBelongsToVersionAsync(dbContext, versionId, request.ComponentId))
        {
            return Results.BadRequest(new { message = "Data store must reference a component in the version." });
        }

        var store = new DataStore
        {
            Id = Guid.NewGuid(),
            AiSystemVersionId = versionId,
            ComponentId = request.ComponentId,
            StorageType = request.StorageType,
            Region = request.Region,
            RetentionDays = request.RetentionDays,
            EncryptionAtRest = request.EncryptionAtRest,
            AccessModel = request.AccessModel
        };

        dbContext.DataStores.Add(store);
        await dbContext.SaveChangesAsync();
        return Results.Created($"/versions/{versionId}/architecture/stores/{store.Id}", store);
    }

    private static async Task<IResult> UpdateStoreAsync([FromRoute] Guid versionId, [FromRoute] Guid storeId, [FromBody] UpsertStoreRequest request, NormyxDbContext dbContext, ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);
        if (!await VersionBelongsToTenantAsync(dbContext, versionId, tenantId))
        {
            return Results.NotFound();
        }

        if (!await ComponentBelongsToVersionAsync(dbContext, versionId, request.ComponentId))
        {
            return Results.BadRequest(new { message = "Data store must reference a component in the version." });
        }

        var store = await dbContext.DataStores.FirstOrDefaultAsync(x => x.Id == storeId && x.AiSystemVersionId == versionId);
        if (store is null)
        {
            return Results.NotFound();
        }

        store.ComponentId = request.ComponentId;
        store.StorageType = request.StorageType;
        store.Region = request.Region;
        store.RetentionDays = request.RetentionDays;
        store.EncryptionAtRest = request.EncryptionAtRest;
        store.AccessModel = request.AccessModel;

        await dbContext.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteStoreAsync([FromRoute] Guid versionId, [FromRoute] Guid storeId, NormyxDbContext dbContext, ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);
        if (!await VersionBelongsToTenantAsync(dbContext, versionId, tenantId))
        {
            return Results.NotFound();
        }

        var store = await dbContext.DataStores.FirstOrDefaultAsync(x => x.Id == storeId && x.AiSystemVersionId == versionId);
        if (store is null)
        {
            return Results.NotFound();
        }

        dbContext.DataStores.Remove(store);
        await dbContext.SaveChangesAsync();
        return Results.NoContent();
    }
}
