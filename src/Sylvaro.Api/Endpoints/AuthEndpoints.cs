using System.Security.Cryptography;
using System.Text;
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
        var group = app.MapGroup("/auth").WithTags("Auth").RequireRateLimiting("auth").WithRequestValidation();

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
        IOptions<JwtOptions> jwtOptions,
        HttpContext httpContext)
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

        var (refreshTokenRaw, refreshExpiresAt) = IssueRefreshToken(
            dbContext,
            jwtTokenService,
            user.Id,
            jwtOptions.Value.RefreshTokenDays,
            httpContext);

        await dbContext.SaveChangesAsync();

        var response = new AuthResponse(
            jwtTokenService.CreateAccessToken(user, [RoleNames.Admin]),
            refreshTokenRaw,
            refreshExpiresAt);

        return Results.Ok(response);
    }

    private static async Task<IResult> LoginAsync(
        [FromBody] LoginRequest request,
        NormyxDbContext dbContext,
        IJwtTokenService jwtTokenService,
        IOptions<JwtOptions> jwtOptions,
        HttpContext httpContext)
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

        var (refreshTokenRaw, refreshExpiresAt) = IssueRefreshToken(
            dbContext,
            jwtTokenService,
            user.Id,
            jwtOptions.Value.RefreshTokenDays,
            httpContext);

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
        IOptions<JwtOptions> jwtOptions,
        HttpContext httpContext)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return Results.Unauthorized();
        }

        var tokenHash = HashToken(request.RefreshToken);
        var tokenSnapshot = await dbContext.RefreshTokens
            .AsNoTracking()
            .Where(x => x.Token == tokenHash)
            .Select(x => new
            {
                x.Id,
                x.UserId,
                x.ExpiresAt,
                x.RevokedAt
            })
            .FirstOrDefaultAsync();

        var now = DateTimeOffset.UtcNow;
        if (tokenSnapshot is null || tokenSnapshot.RevokedAt is not null || tokenSnapshot.ExpiresAt < now)
        {
            return Results.Unauthorized();
        }

        var revokedRows = await dbContext.RefreshTokens
            .Where(x => x.Id == tokenSnapshot.Id && x.RevokedAt == null && x.ExpiresAt >= now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.RevokedAt, now));

        if (revokedRows == 0)
        {
            return Results.Unauthorized();
        }

        var user = await dbContext.Users.FirstOrDefaultAsync(x => x.Id == tokenSnapshot.UserId && x.DisabledAt == null);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var roles = await dbContext.UserRoles
            .Where(ur => ur.UserId == tokenSnapshot.UserId)
            .Join(dbContext.Roles, ur => ur.RoleId, r => r.Id, (_, role) => role.Name)
            .ToListAsync();

        var (newRefresh, refreshExpiresAt) = IssueRefreshToken(
            dbContext,
            jwtTokenService,
            tokenSnapshot.UserId,
            jwtOptions.Value.RefreshTokenDays,
            httpContext);

        await dbContext.SaveChangesAsync();

        return Results.Ok(new AuthResponse(
            jwtTokenService.CreateAccessToken(user, roles),
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

        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return Results.NoContent();
        }

        var tokenHash = HashToken(request.RefreshToken);
        await dbContext.RefreshTokens
            .Where(x => x.Token == tokenHash && x.UserId == currentUser.UserId.Value && x.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));

        return Results.NoContent();
    }

    private static string HashToken(string token)
    {
        var bytes = Encoding.UTF8.GetBytes(token.Trim());
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private static (string RefreshToken, DateTimeOffset ExpiresAt) IssueRefreshToken(
        NormyxDbContext dbContext,
        IJwtTokenService jwtTokenService,
        Guid userId,
        int refreshTokenDays,
        HttpContext httpContext)
    {
        var refreshTokenRaw = jwtTokenService.CreateRefreshToken();
        var refreshExpiresAt = DateTimeOffset.UtcNow.AddDays(refreshTokenDays);

        dbContext.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Token = HashToken(refreshTokenRaw),
            CreatedIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            UserAgent = NormalizeUserAgent(httpContext.Request.Headers.UserAgent.ToString()),
            ExpiresAt = refreshExpiresAt
        });

        return (refreshTokenRaw, refreshExpiresAt);
    }

    private static string NormalizeUserAgent(string userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return "unknown";
        }

        var sanitized = userAgent.Trim();
        return sanitized.Length <= 256 ? sanitized : sanitized[..256];
    }
}
