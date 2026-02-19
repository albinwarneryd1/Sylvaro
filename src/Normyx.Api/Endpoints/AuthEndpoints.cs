using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Normyx.Api.Contracts.Auth;
using Normyx.Application.Abstractions;
using Normyx.Application.Security;
using Normyx.Domain.Entities;
using Normyx.Infrastructure.Auth;
using Normyx.Infrastructure.Persistence;

namespace Normyx.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").WithTags("Auth");

        group.MapPost("/register", RegisterAsync).AllowAnonymous();
        group.MapPost("/login", LoginAsync).AllowAnonymous();
        group.MapPost("/refresh", RefreshAsync).AllowAnonymous();
        group.MapPost("/logout", LogoutAsync).RequireAuthorization();

        return app;
    }

    private static async Task<IResult> RegisterAsync(
        [FromBody] RegisterRequest request,
        NormyxDbContext dbContext,
        IJwtTokenService jwtTokenService,
        IOptions<JwtOptions> jwtOptions)
    {
        var existingTenant = await dbContext.Tenants.FirstOrDefaultAsync(x => x.Name == request.TenantName);
        if (existingTenant is not null)
        {
            return Results.Conflict(new { message = "Tenant name already exists" });
        }

        var tenant = new Tenant { Id = Guid.NewGuid(), Name = request.TenantName };
        var user = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Email = request.Email.Trim().ToLowerInvariant(),
            DisplayName = request.DisplayName
        };

        var hasher = new PasswordHasher<User>();
        user.PasswordHash = hasher.HashPassword(user, request.Password);

        var adminRole = await dbContext.Roles.FirstOrDefaultAsync(x => x.Name == RoleNames.Admin);
        if (adminRole is null)
        {
            adminRole = new Role { Id = Guid.NewGuid(), Name = RoleNames.Admin };
            dbContext.Roles.Add(adminRole);
        }

        dbContext.Tenants.Add(tenant);
        dbContext.Users.Add(user);
        dbContext.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = adminRole.Id });

        var refreshTokenRaw = jwtTokenService.CreateRefreshToken();
        dbContext.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Token = refreshTokenRaw,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(jwtOptions.Value.RefreshTokenDays)
        });

        await dbContext.SaveChangesAsync();

        var response = new AuthResponse(
            jwtTokenService.CreateAccessToken(user, [RoleNames.Admin]),
            refreshTokenRaw,
            DateTimeOffset.UtcNow.AddDays(jwtOptions.Value.RefreshTokenDays));

        return Results.Ok(response);
    }

    private static async Task<IResult> LoginAsync(
        [FromBody] LoginRequest request,
        NormyxDbContext dbContext,
        IJwtTokenService jwtTokenService,
        IOptions<JwtOptions> jwtOptions)
    {
        var tenant = await dbContext.Tenants.FirstOrDefaultAsync(x => x.Name == request.TenantName);
        if (tenant is null)
        {
            return Results.Unauthorized();
        }

        var user = await dbContext.Users
            .FirstOrDefaultAsync(x => x.TenantId == tenant.Id && x.Email == request.Email.Trim().ToLowerInvariant());

        if (user is null || user.DisabledAt is not null)
        {
            return Results.Unauthorized();
        }

        var hasher = new PasswordHasher<User>();
        var verify = hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verify == PasswordVerificationResult.Failed)
        {
            return Results.Unauthorized();
        }

        var roles = await dbContext.UserRoles
            .Where(ur => ur.UserId == user.Id)
            .Join(dbContext.Roles, ur => ur.RoleId, r => r.Id, (_, role) => role.Name)
            .ToListAsync();

        var refreshTokenRaw = jwtTokenService.CreateRefreshToken();
        var refreshExpiresAt = DateTimeOffset.UtcNow.AddDays(jwtOptions.Value.RefreshTokenDays);

        dbContext.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Token = refreshTokenRaw,
            ExpiresAt = refreshExpiresAt
        });

        await dbContext.SaveChangesAsync();

        return Results.Ok(new AuthResponse(
            jwtTokenService.CreateAccessToken(user, roles),
            refreshTokenRaw,
            refreshExpiresAt));
    }

    private static async Task<IResult> RefreshAsync(
        [FromBody] RefreshRequest request,
        NormyxDbContext dbContext,
        IJwtTokenService jwtTokenService,
        IOptions<JwtOptions> jwtOptions)
    {
        var refreshToken = await dbContext.RefreshTokens
            .Include(x => x.User)
            .FirstOrDefaultAsync(x => x.Token == request.RefreshToken);

        if (refreshToken is null || refreshToken.RevokedAt is not null || refreshToken.ExpiresAt < DateTimeOffset.UtcNow)
        {
            return Results.Unauthorized();
        }

        var roles = await dbContext.UserRoles
            .Where(ur => ur.UserId == refreshToken.UserId)
            .Join(dbContext.Roles, ur => ur.RoleId, r => r.Id, (_, role) => role.Name)
            .ToListAsync();

        refreshToken.RevokedAt = DateTimeOffset.UtcNow;

        var newRefresh = jwtTokenService.CreateRefreshToken();
        var refreshExpiresAt = DateTimeOffset.UtcNow.AddDays(jwtOptions.Value.RefreshTokenDays);
        dbContext.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = refreshToken.UserId,
            Token = newRefresh,
            ExpiresAt = refreshExpiresAt
        });

        await dbContext.SaveChangesAsync();

        return Results.Ok(new AuthResponse(
            jwtTokenService.CreateAccessToken(refreshToken.User, roles),
            newRefresh,
            refreshExpiresAt));
    }

    private static async Task<IResult> LogoutAsync(
        [FromBody] LogoutRequest request,
        NormyxDbContext dbContext,
        ICurrentUserContext currentUser)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
        {
            return Results.Unauthorized();
        }

        var refreshToken = await dbContext.RefreshTokens
            .FirstOrDefaultAsync(x => x.Token == request.RefreshToken && x.UserId == currentUser.UserId.Value);

        if (refreshToken is null)
        {
            return Results.NotFound();
        }

        refreshToken.RevokedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync();

        return Results.NoContent();
    }
}
