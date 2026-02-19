namespace Normyx.Api.Contracts.Auth;

using System.ComponentModel.DataAnnotations;

public record RegisterRequest(
    [property: Required, StringLength(120, MinimumLength = 2)] string TenantName,
    [property: Required, EmailAddress, StringLength(255)] string Email,
    [property: Required, StringLength(120, MinimumLength = 2)] string DisplayName,
    [property: Required, StringLength(128, MinimumLength = 10)] string Password);

public record LoginRequest(
    [property: Required, StringLength(120, MinimumLength = 2)] string TenantName,
    [property: Required, EmailAddress, StringLength(255)] string Email,
    [property: Required, StringLength(128, MinimumLength = 10)] string Password);

public record RefreshRequest(
    [property: Required, MinLength(24)] string RefreshToken);

public record LogoutRequest(
    [property: Required, MinLength(24)] string RefreshToken);

public record AuthResponse(string AccessToken, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt);
