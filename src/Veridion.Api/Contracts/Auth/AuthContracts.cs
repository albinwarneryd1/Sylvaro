namespace Veridion.Api.Contracts.Auth;

public record RegisterRequest(string TenantName, string Email, string DisplayName, string Password);
public record LoginRequest(string TenantName, string Email, string Password);
public record RefreshRequest(string RefreshToken);
public record LogoutRequest(string RefreshToken);
public record AuthResponse(string AccessToken, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt);
