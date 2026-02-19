using Microsoft.JSInterop;

namespace Sylvaro.Web.Services;

public class AuthSession
{
    private const string StorageKey = "sylvaro.auth.session";
    private bool _initialized;

    public string AccessToken { get; private set; } = string.Empty;
    public string RefreshToken { get; private set; } = string.Empty;
    public DateTimeOffset RefreshTokenExpiresAt { get; private set; }

    public string TenantName { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public DateTimeOffset LastAuthenticatedAt { get; private set; }

    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(AccessToken);
    public bool IsInitialized => _initialized;

    public event Action? Changed;

    public void SetSession(string tenantName, string email, string accessToken, string refreshToken, DateTimeOffset refreshTokenExpiresAt)
    {
        TenantName = tenantName;
        Email = email;
        AccessToken = accessToken;
        RefreshToken = refreshToken;
        RefreshTokenExpiresAt = refreshTokenExpiresAt;
        LastAuthenticatedAt = DateTimeOffset.UtcNow;
        Changed?.Invoke();
    }

    public void Clear()
    {
        AccessToken = string.Empty;
        RefreshToken = string.Empty;
        RefreshTokenExpiresAt = default;
        TenantName = string.Empty;
        Email = string.Empty;
        LastAuthenticatedAt = default;
        Changed?.Invoke();
    }

    public async Task EnsureInitializedAsync(IJSRuntime jsRuntime)
    {
        if (_initialized)
        {
            return;
        }

        // Security-default behavior: always start at login screen and do not auto-restore
        // an authenticated session from browser storage.
        try
        {
            await jsRuntime.InvokeVoidAsync("localStorage.removeItem", StorageKey);
        }
        catch
        {
            // Ignore storage errors and continue with in-memory defaults.
        }
        finally
        {
            _initialized = true;
            Changed?.Invoke();
        }
    }

    public Task PersistAsync(IJSRuntime jsRuntime)
    {
        // Keep session in-memory only for current runtime; no browser persistence.
        return Task.CompletedTask;
    }

    public async Task ClearPersistedAsync(IJSRuntime jsRuntime)
    {
        Clear();

        try
        {
            await jsRuntime.InvokeVoidAsync("localStorage.removeItem", StorageKey);
        }
        catch
        {
            // Ignore storage cleanup failures.
        }
    }
}
