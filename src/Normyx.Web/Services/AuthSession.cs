using System.Text.Json;
using Microsoft.JSInterop;

namespace Normyx.Web.Services;

public class AuthSession
{
    private const string StorageKey = "normyx.auth.session";
    private bool _initialized;

    public string AccessToken { get; private set; } = string.Empty;
    public string RefreshToken { get; private set; } = string.Empty;
    public DateTimeOffset RefreshTokenExpiresAt { get; private set; }

    public string TenantName { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;

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

    public async Task EnsureInitializedAsync(IJSRuntime jsRuntime)
    {
        if (_initialized)
        {
            return;
        }

        try
        {
            var raw = await jsRuntime.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            if (!string.IsNullOrWhiteSpace(raw))
            {
                var persisted = JsonSerializer.Deserialize<PersistedSession>(raw);
                if (persisted is not null && !string.IsNullOrWhiteSpace(persisted.AccessToken))
                {
                    TenantName = persisted.TenantName;
                    Email = persisted.Email;
                    AccessToken = persisted.AccessToken;
                    RefreshToken = persisted.RefreshToken;
                    RefreshTokenExpiresAt = persisted.RefreshTokenExpiresAt;
                }
            }
        }
        catch
        {
            // Keep in-memory defaults if browser storage is unavailable.
        }
        finally
        {
            _initialized = true;
            Changed?.Invoke();
        }
    }

    public async Task PersistAsync(IJSRuntime jsRuntime)
    {
        var payload = JsonSerializer.Serialize(new PersistedSession(
            TenantName,
            Email,
            AccessToken,
            RefreshToken,
            RefreshTokenExpiresAt));
        await jsRuntime.InvokeVoidAsync("localStorage.setItem", StorageKey, payload);
    }

    public async Task ClearPersistedAsync(IJSRuntime jsRuntime)
    {
        Clear();
        await jsRuntime.InvokeVoidAsync("localStorage.removeItem", StorageKey);
    }

    private sealed record PersistedSession(
        string TenantName,
        string Email,
        string AccessToken,
        string RefreshToken,
        DateTimeOffset RefreshTokenExpiresAt);
}
