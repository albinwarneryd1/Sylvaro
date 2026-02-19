using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Normyx.Application.Abstractions;
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

    private sealed record SessionDto(
        Guid Id,
        string SessionRef,
        string Ip,
        string UserAgent,
        DateTimeOffset CreatedAt,
        DateTimeOffset ExpiresAt);

    private sealed record RevokeOtherSessionsRequest([property: MaxLength(2048)] string? CurrentRefreshToken);

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
}
