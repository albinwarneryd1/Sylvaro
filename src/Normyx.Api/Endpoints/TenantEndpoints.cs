using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Normyx.Api.Utilities;
using Normyx.Application.Abstractions;
using Normyx.Application.Security;
using Normyx.Domain.Entities;
using Normyx.Infrastructure.Persistence;

namespace Normyx.Api.Endpoints;

public static class TenantEndpoints
{
    public static IEndpointRouteBuilder MapTenantEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/tenants").WithTags("Tenants").RequireAuthorization().WithRequestValidation();

        group.MapGet("/me", GetCurrentTenantAsync);
        group.MapPut("/me", UpdateCurrentTenantAsync).RequireAuthorization(new AuthorizeAttribute { Roles = RoleNames.Admin });
        group.MapGet("/roles", ListRolesAsync);
        group.MapGet("/users", ListUsersAsync).RequireAuthorization(new AuthorizeAttribute { Roles = RoleNames.Admin });
        group.MapPost("/users", CreateUserAsync).RequireAuthorization(new AuthorizeAttribute { Roles = RoleNames.Admin });
        group.MapPut("/users/{userId:guid}/roles", UpdateUserRolesAsync).RequireAuthorization(new AuthorizeAttribute { Roles = RoleNames.Admin });
        group.MapGet("/policy-packs", ListPolicyPacksAsync).RequireAuthorization(new AuthorizeAttribute { Roles = RoleNames.Admin });
        group.MapPut("/policy-packs/{policyPackId:guid}/enabled", SetPolicyPackEnabledAsync).RequireAuthorization(new AuthorizeAttribute { Roles = RoleNames.Admin });

        return app;
    }

    private static async Task<IResult> GetCurrentTenantAsync(NormyxDbContext dbContext, ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);

        var tenant = await dbContext.Tenants
            .Where(x => x.Id == tenantId)
            .Select(x => new { x.Id, x.Name, x.CreatedAt })
            .FirstOrDefaultAsync();

        return tenant is null ? Results.NotFound() : Results.Ok(tenant);
    }

    private record UpdateTenantRequest([property: Required, StringLength(120, MinimumLength = 2)] string Name);

    private static async Task<IResult> UpdateCurrentTenantAsync([FromBody] UpdateTenantRequest request, NormyxDbContext dbContext, ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);
        var normalizedName = request.Name?.Trim() ?? string.Empty;

        if (normalizedName.Length < 2)
        {
            return Results.BadRequest(new { message = "Tenant name must be at least 2 characters." });
        }

        var tenant = await dbContext.Tenants.FirstOrDefaultAsync(x => x.Id == tenantId);
        if (tenant is null)
        {
            return Results.NotFound();
        }

        tenant.Name = normalizedName;
        await dbContext.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> ListRolesAsync(NormyxDbContext dbContext)
    {
        var roles = await dbContext.Roles
            .OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Name })
            .ToListAsync();

        return Results.Ok(roles);
    }

    private static async Task<IResult> ListUsersAsync(NormyxDbContext dbContext, ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);

        var users = await dbContext.Users
            .Where(x => x.TenantId == tenantId)
            .Include(x => x.UserRoles)
            .ThenInclude(x => x.Role)
            .ToListAsync();

        var result = users.Select(x => new
        {
            x.Id,
            x.Email,
            x.DisplayName,
            x.CreatedAt,
            x.DisabledAt,
            Roles = x.UserRoles
                .Select(ur => ur.Role.Name)
                .OrderBy(name => name)
                .ToList()
        });

        return Results.Ok(result);
    }

    private record CreateUserRequest(
        [property: Required, EmailAddress, StringLength(255)] string Email,
        [property: Required, StringLength(120, MinimumLength = 2)] string DisplayName,
        [property: Required, StringLength(128, MinimumLength = 10)] string Password,
        [property: MinLength(1)] string[] Roles);

    private static async Task<IResult> CreateUserAsync(
        [FromBody] CreateUserRequest request,
        NormyxDbContext dbContext,
        ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        if (await dbContext.Users.AnyAsync(x => x.TenantId == tenantId && x.Email == normalizedEmail))
        {
            return Results.Conflict(new { message = "User already exists" });
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Email = normalizedEmail,
            DisplayName = request.DisplayName
        };

        var hasher = new PasswordHasher<User>();
        user.PasswordHash = hasher.HashPassword(user, request.Password);

        var roles = await dbContext.Roles
            .Where(x => request.Roles.Contains(x.Name))
            .ToListAsync();

        dbContext.Users.Add(user);
        foreach (var role in roles)
        {
            dbContext.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        }

        await dbContext.SaveChangesAsync();
        return Results.Created($"/tenants/users/{user.Id}", new { user.Id, user.Email, user.DisplayName });
    }

    private record UpdateRolesRequest([property: MinLength(1)] string[] Roles);

    private static async Task<IResult> ListPolicyPacksAsync(NormyxDbContext dbContext, ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);

        var policyPacks = await dbContext.PolicyPacks
            .OrderBy(x => x.Scope)
            .ThenBy(x => x.Name)
            .Select(pack => new
            {
                pack.Id,
                pack.Name,
                pack.Version,
                Scope = pack.Scope.ToString(),
                enabled = dbContext.TenantPolicyPackSelections
                    .Where(sel => sel.TenantId == tenantId && sel.PolicyPackId == pack.Id)
                    .Select(sel => (bool?)sel.IsEnabled)
                    .FirstOrDefault() ?? false
            })
            .ToListAsync();

        return Results.Ok(policyPacks);
    }

    private record SetPolicyPackEnabledRequest(bool Enabled);

    private static async Task<IResult> SetPolicyPackEnabledAsync(
        [FromRoute] Guid policyPackId,
        [FromBody] SetPolicyPackEnabledRequest request,
        NormyxDbContext dbContext,
        ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);

        var exists = await dbContext.PolicyPacks.AnyAsync(x => x.Id == policyPackId);
        if (!exists)
        {
            return Results.NotFound();
        }

        var selection = await dbContext.TenantPolicyPackSelections
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.PolicyPackId == policyPackId);

        if (selection is null)
        {
            selection = new TenantPolicyPackSelection
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                PolicyPackId = policyPackId
            };
            dbContext.TenantPolicyPackSelections.Add(selection);
        }

        selection.IsEnabled = request.Enabled;
        selection.UpdatedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> UpdateUserRolesAsync(
        [FromRoute] Guid userId,
        [FromBody] UpdateRolesRequest request,
        NormyxDbContext dbContext,
        ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);

        var user = await dbContext.Users.FirstOrDefaultAsync(x => x.Id == userId && x.TenantId == tenantId);
        if (user is null)
        {
            return Results.NotFound();
        }

        var roleIds = await dbContext.Roles
            .Where(x => request.Roles.Contains(x.Name))
            .Select(x => x.Id)
            .ToListAsync();

        var existingRoles = dbContext.UserRoles.Where(x => x.UserId == userId);
        dbContext.UserRoles.RemoveRange(existingRoles);
        dbContext.UserRoles.AddRange(roleIds.Select(roleId => new UserRole { UserId = userId, RoleId = roleId }));

        await dbContext.SaveChangesAsync();
        return Results.NoContent();
    }
}
