namespace Normyx.Web.Services;

public class AuthSession
{
    public string AccessToken { get; private set; } = string.Empty;
    public string RefreshToken { get; private set; } = string.Empty;
    public DateTimeOffset RefreshTokenExpiresAt { get; private set; }

    public string TenantName { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;

    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(AccessToken);

    public event Action? Changed;

    public void SetSession(string tenantName, string email, string accessToken, string refreshToken, DateTimeOffset refreshTokenExpiresAt)
    {
        TenantName = tenantName;
        Email = email;
        AccessToken = accessToken;
        RefreshToken = refreshToken;
        RefreshTokenExpiresAt = refreshTokenExpiresAt;
        Changed?.Invoke();
    }

    public void Clear()
    {
        AccessToken = string.Empty;
        RefreshToken = string.Empty;
        RefreshTokenExpiresAt = default;
        TenantName = string.Empty;
        Email = string.Empty;
        Changed?.Invoke();
    }
}
