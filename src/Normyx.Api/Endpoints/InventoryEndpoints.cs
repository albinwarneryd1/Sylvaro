using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Normyx.Api.Utilities;
using Normyx.Application.Abstractions;
using Normyx.Domain.Entities;
using Normyx.Infrastructure.Persistence;

namespace Normyx.Api.Endpoints;

public static class InventoryEndpoints
{
    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/versions/{versionId:guid}/inventory").WithTags("Inventory").RequireAuthorization();

        group.MapGet("", GetInventoryAsync);

        group.MapPost("/data-items", AddDataItemAsync);
        group.MapPut("/data-items/{itemId:guid}", UpdateDataItemAsync);
        group.MapDelete("/data-items/{itemId:guid}", DeleteDataItemAsync);

        group.MapPost("/vendors", AddVendorAsync);
        group.MapPut("/vendors/{vendorId:guid}", UpdateVendorAsync);
        group.MapDelete("/vendors/{vendorId:guid}", DeleteVendorAsync);

        return app;
    }

    private static async Task<bool> VersionBelongsToTenantAsync(NormyxDbContext dbContext, Guid versionId, Guid tenantId)
        => await dbContext.AiSystemVersions.AnyAsync(v => v.Id == versionId && v.AiSystem.TenantId == tenantId);

    private static async Task<IResult> GetInventoryAsync([FromRoute] Guid versionId, NormyxDbContext dbContext, ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);
        if (!await VersionBelongsToTenantAsync(dbContext, versionId, tenantId))
        {
            return Results.NotFound();
        }

        var dataItems = await dbContext.DataInventoryItems.Where(x => x.AiSystemVersionId == versionId).ToListAsync();
        var vendors = await dbContext.Vendors.Where(x => x.AiSystemVersionId == versionId).ToListAsync();

        return Results.Ok(new { dataItems, vendors });
    }

    private record UpsertDataItemRequest(string DataCategory, bool ContainsPersonalData, bool SpecialCategory, string Source, string LawfulBasis, int RetentionDays, bool TransferOutsideEu, string Notes);

    private static async Task<IResult> AddDataItemAsync([FromRoute] Guid versionId, [FromBody] UpsertDataItemRequest request, NormyxDbContext dbContext, ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);
        if (!await VersionBelongsToTenantAsync(dbContext, versionId, tenantId))
        {
            return Results.NotFound();
        }

        var item = new DataInventoryItem
        {
            Id = Guid.NewGuid(),
            AiSystemVersionId = versionId,
            DataCategory = request.DataCategory,
            ContainsPersonalData = request.ContainsPersonalData,
            SpecialCategory = request.SpecialCategory,
            Source = request.Source,
            LawfulBasis = request.LawfulBasis,
            RetentionDays = request.RetentionDays,
            TransferOutsideEu = request.TransferOutsideEu,
            Notes = request.Notes
        };

        dbContext.DataInventoryItems.Add(item);
        await dbContext.SaveChangesAsync();

        return Results.Created($"/versions/{versionId}/inventory/data-items/{item.Id}", item);
    }

    private static async Task<IResult> UpdateDataItemAsync([FromRoute] Guid versionId, [FromRoute] Guid itemId, [FromBody] UpsertDataItemRequest request, NormyxDbContext dbContext, ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);
        if (!await VersionBelongsToTenantAsync(dbContext, versionId, tenantId))
        {
            return Results.NotFound();
        }

        var item = await dbContext.DataInventoryItems.FirstOrDefaultAsync(x => x.Id == itemId && x.AiSystemVersionId == versionId);
        if (item is null)
        {
            return Results.NotFound();
        }

        item.DataCategory = request.DataCategory;
        item.ContainsPersonalData = request.ContainsPersonalData;
        item.SpecialCategory = request.SpecialCategory;
        item.Source = request.Source;
        item.LawfulBasis = request.LawfulBasis;
        item.RetentionDays = request.RetentionDays;
        item.TransferOutsideEu = request.TransferOutsideEu;
        item.Notes = request.Notes;

        await dbContext.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteDataItemAsync([FromRoute] Guid versionId, [FromRoute] Guid itemId, NormyxDbContext dbContext, ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);
        if (!await VersionBelongsToTenantAsync(dbContext, versionId, tenantId))
        {
            return Results.NotFound();
        }

        var item = await dbContext.DataInventoryItems.FirstOrDefaultAsync(x => x.Id == itemId && x.AiSystemVersionId == versionId);
        if (item is null)
        {
            return Results.NotFound();
        }

        dbContext.DataInventoryItems.Remove(item);
        await dbContext.SaveChangesAsync();

        return Results.NoContent();
    }

    private record UpsertVendorRequest(string Name, string ServiceType, string Region, string[] SubProcessors, bool DpaInPlace, string Notes);

    private static async Task<IResult> AddVendorAsync([FromRoute] Guid versionId, [FromBody] UpsertVendorRequest request, NormyxDbContext dbContext, ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);
        if (!await VersionBelongsToTenantAsync(dbContext, versionId, tenantId))
        {
            return Results.NotFound();
        }

        var vendor = new Vendor
        {
            Id = Guid.NewGuid(),
            AiSystemVersionId = versionId,
            Name = request.Name,
            ServiceType = request.ServiceType,
            Region = request.Region,
            SubProcessors = request.SubProcessors,
            DpaInPlace = request.DpaInPlace,
            Notes = request.Notes
        };

        dbContext.Vendors.Add(vendor);
        await dbContext.SaveChangesAsync();
        return Results.Created($"/versions/{versionId}/inventory/vendors/{vendor.Id}", vendor);
    }

    private static async Task<IResult> UpdateVendorAsync([FromRoute] Guid versionId, [FromRoute] Guid vendorId, [FromBody] UpsertVendorRequest request, NormyxDbContext dbContext, ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);
        if (!await VersionBelongsToTenantAsync(dbContext, versionId, tenantId))
        {
            return Results.NotFound();
        }

        var vendor = await dbContext.Vendors.FirstOrDefaultAsync(x => x.Id == vendorId && x.AiSystemVersionId == versionId);
        if (vendor is null)
        {
            return Results.NotFound();
        }

        vendor.Name = request.Name;
        vendor.ServiceType = request.ServiceType;
        vendor.Region = request.Region;
        vendor.SubProcessors = request.SubProcessors;
        vendor.DpaInPlace = request.DpaInPlace;
        vendor.Notes = request.Notes;

        await dbContext.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteVendorAsync([FromRoute] Guid versionId, [FromRoute] Guid vendorId, NormyxDbContext dbContext, ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);
        if (!await VersionBelongsToTenantAsync(dbContext, versionId, tenantId))
        {
            return Results.NotFound();
        }

        var vendor = await dbContext.Vendors.FirstOrDefaultAsync(x => x.Id == vendorId && x.AiSystemVersionId == versionId);
        if (vendor is null)
        {
            return Results.NotFound();
        }

        dbContext.Vendors.Remove(vendor);
        await dbContext.SaveChangesAsync();
        return Results.NoContent();
    }
}
