using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Normyx.Application.Abstractions;
using Normyx.Application.Security;
using Normyx.Infrastructure.Persistence;

namespace Normyx.Api.Endpoints;

public static class SecurityEndpoints
{
    public static IEndpointRouteBuilder MapSecurityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/security")
            .WithTags("Security")
            .RequireAuthorization()
            .WithRequestValidation();

        group.MapGet("/sessions", ListSessionsAsync);
        group.MapDelete("/sessions/{sessionId:guid}", RevokeSessionAsync);
        group.MapPost("/sessions/revoke-others", RevokeOtherSessionsAsync);
        group.MapGet("/api-tokens", ListApiTokensAsync).RequireAuthorization(new AuthorizeAttribute { Roles = RoleNames.Admin });
        group.MapPost("/api-tokens", CreateApiTokenAsync).RequireAuthorization(new AuthorizeAttribute { Roles = RoleNames.Admin });
        group.MapDelete("/api-tokens/{tokenId:guid}", RevokeApiTokenAsync).RequireAuthorization(new AuthorizeAttribute { Roles = RoleNames.Admin });

        return app;
    }

    private static async Task<IResult> ListSessionsAsync(
        NormyxDbContext dbContext,
        ICurrentUserContext currentUser)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
        {
            return Results.Unauthorized();
        }

        var now = DateTimeOffset.UtcNow;
        var sessionRows = await dbContext.RefreshTokens
            .AsNoTracking()
            .Where(x => x.UserId == currentUser.UserId.Value && x.RevokedAt == null && x.ExpiresAt >= now)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new
            {
                x.Id,
                x.Token,
                x.CreatedIp,
                x.UserAgent,
                x.CreatedAt,
                x.ExpiresAt
            })
            .ToListAsync();

        var sessions = sessionRows
            .Select(x => new SessionDto(
                x.Id,
                BuildSessionRef(x.Token),
                x.CreatedIp,
                x.UserAgent,
                x.CreatedAt,
                x.ExpiresAt))
            .ToList();

        return Results.Ok(sessions);
    }

    private static async Task<IResult> RevokeSessionAsync(
        [FromRoute] Guid sessionId,
        NormyxDbContext dbContext,
        ICurrentUserContext currentUser)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
        {
            return Results.Unauthorized();
        }

        var now = DateTimeOffset.UtcNow;
        var updated = await dbContext.RefreshTokens
            .Where(x => x.Id == sessionId && x.UserId == currentUser.UserId.Value && x.RevokedAt == null && x.ExpiresAt >= now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.RevokedAt, now));

        return updated == 0 ? Results.NotFound() : Results.NoContent();
    }

    private static async Task<IResult> RevokeOtherSessionsAsync(
        [FromBody] RevokeOtherSessionsRequest request,
        NormyxDbContext dbContext,
        ICurrentUserContext currentUser)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
        {
            return Results.Unauthorized();
        }

        var now = DateTimeOffset.UtcNow;
        var currentHash = string.IsNullOrWhiteSpace(request.CurrentRefreshToken)
            ? null
            : AuthEndpointsHashShim.HashToken(request.CurrentRefreshToken);

        var query = dbContext.RefreshTokens
            .Where(x => x.UserId == currentUser.UserId.Value && x.RevokedAt == null && x.ExpiresAt >= now);

        if (!string.IsNullOrWhiteSpace(currentHash))
        {
            query = query.Where(x => x.Token != currentHash);
        }

        var updated = await query.ExecuteUpdateAsync(setters => setters.SetProperty(x => x.RevokedAt, now));
        return Results.Ok(new { revokedSessions = updated });
    }

    private static async Task<IResult> ListApiTokensAsync(
        NormyxDbContext dbContext,
        ICurrentUserContext currentUser)
    {
        if (!currentUser.IsAuthenticated || currentUser.TenantId is null)
        {
            return Results.Unauthorized();
        }

        var tokens = await dbContext.ApiTokens
            .AsNoTracking()
            .Where(x => x.TenantId == currentUser.TenantId.Value)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.Scope,
                x.TokenPrefix,
                x.CreatedByUserId,
                x.CreatedAt,
                x.RevokedAt
            })
            .ToListAsync();

        return Results.Ok(tokens);
    }

    private static async Task<IResult> CreateApiTokenAsync(
        [FromBody] CreateApiTokenRequest request,
        NormyxDbContext dbContext,
        ICurrentUserContext currentUser)
    {
        if (!currentUser.IsAuthenticated || currentUser.TenantId is null || currentUser.UserId is null)
        {
            return Results.Unauthorized();
        }

        var duplicateName = await dbContext.ApiTokens.AnyAsync(x =>
            x.TenantId == currentUser.TenantId.Value &&
            x.Name == request.Name &&
            x.RevokedAt == null);
        if (duplicateName)
        {
            return Results.Conflict(new { message = "An active API token with this name already exists." });
        }

        var tokenRaw = $"nxa_{Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant()}";
        var token = new Normyx.Domain.Entities.ApiToken
        {
            Id = Guid.NewGuid(),
            TenantId = currentUser.TenantId.Value,
            CreatedByUserId = currentUser.UserId.Value,
            Name = request.Name.Trim(),
            Scope = request.Scope.Trim(),
            TokenHash = AuthEndpointsHashShim.HashToken(tokenRaw),
            TokenPrefix = BuildTokenPrefix(tokenRaw),
            CreatedAt = DateTimeOffset.UtcNow
        };

        dbContext.ApiTokens.Add(token);
        await dbContext.SaveChangesAsync();

        return Results.Created($"/security/api-tokens/{token.Id}", new
        {
            token.Id,
            token.Name,
            token.Scope,
            token.TokenPrefix,
            token.CreatedByUserId,
            token.CreatedAt,
            tokenValue = tokenRaw
        });
    }

    private static async Task<IResult> RevokeApiTokenAsync(
        [FromRoute] Guid tokenId,
        NormyxDbContext dbContext,
        ICurrentUserContext currentUser)
    {
        if (!currentUser.IsAuthenticated || currentUser.TenantId is null)
        {
            return Results.Unauthorized();
        }

        var now = DateTimeOffset.UtcNow;
        var updated = await dbContext.ApiTokens
            .Where(x => x.Id == tokenId && x.TenantId == currentUser.TenantId.Value && x.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.RevokedAt, now));

        return updated == 0 ? Results.NotFound() : Results.NoContent();
    }

    private sealed record SessionDto(
        Guid Id,
        string SessionRef,
        string Ip,
        string UserAgent,
        DateTimeOffset CreatedAt,
        DateTimeOffset ExpiresAt);

    private sealed record RevokeOtherSessionsRequest([property: MaxLength(2048)] string? CurrentRefreshToken);
    private sealed record CreateApiTokenRequest(
        [property: Required, StringLength(120, MinimumLength = 3)] string Name,
        [property: Required, StringLength(120, MinimumLength = 3)] string Scope);

    // Keeps hashing logic aligned with auth token storage without exposing auth internals as a public API.
    private static class AuthEndpointsHashShim
    {
        public static string HashToken(string token)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(token.Trim());
            var hash = System.Security.Cryptography.SHA256.HashData(bytes);
            return Convert.ToHexString(hash);
        }
    }

    private static string BuildSessionRef(string tokenHash)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            return "sess_unknown";
        }

        var prefix = tokenHash.Length > 10 ? tokenHash[..10] : tokenHash;
        return $"sess_{prefix.ToLowerInvariant()}";
    }

    private static string BuildTokenPrefix(string rawToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return "nxa_unknown";
        }

        return rawToken.Length <= 14 ? rawToken : $"{rawToken[..14]}...";
    }
}
