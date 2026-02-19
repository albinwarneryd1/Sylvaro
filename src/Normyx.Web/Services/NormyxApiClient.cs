using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

namespace Normyx.Web.Services;

public class NormyxApiClient(IHttpClientFactory factory, AuthSession session, IJSRuntime jsRuntime, NavigationManager navigationManager)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private HttpClient Client => factory.CreateClient("NormyxApi");

    public async Task<T?> GetAsync<T>(string path, bool withAuth = true, CancellationToken cancellationToken = default)
    {
        using var response = await SendWithAuthRetryAsync(() => new HttpRequestMessage(HttpMethod.Get, path), withAuth, cancellationToken);
        await EnsureSuccess(response);

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
    }

    public async Task<T?> PostAsync<T>(string path, object payload, bool withAuth = true, CancellationToken cancellationToken = default)
    {
        using var response = await SendWithAuthRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Post, path) { Content = BuildJsonContent(payload) },
            withAuth,
            cancellationToken);
        await EnsureSuccess(response);

        if (response.Content.Headers.ContentLength is 0)
        {
            return default;
        }

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
    }

    public async Task PostAsync(string path, object payload, bool withAuth = true, CancellationToken cancellationToken = default)
    {
        using var response = await SendWithAuthRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Post, path) { Content = BuildJsonContent(payload) },
            withAuth,
            cancellationToken);
        await EnsureSuccess(response);
    }

    public async Task PutAsync(string path, object payload, bool withAuth = true, CancellationToken cancellationToken = default)
    {
        using var response = await SendWithAuthRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Put, path) { Content = BuildJsonContent(payload) },
            withAuth,
            cancellationToken);
        await EnsureSuccess(response);
    }

    public async Task DeleteAsync(string path, bool withAuth = true, CancellationToken cancellationToken = default)
    {
        using var response = await SendWithAuthRetryAsync(() => new HttpRequestMessage(HttpMethod.Delete, path), withAuth, cancellationToken);
        await EnsureSuccess(response);
    }

    public async Task<T?> UploadFileAsync<T>(string path, IBrowserFile file, string? tags = null, CancellationToken cancellationToken = default)
    {
        using var response = await SendWithAuthRetryAsync(() =>
        {
            var form = new MultipartFormDataContent();
            var stream = file.OpenReadStream(20 * 1024 * 1024, cancellationToken);
            var fileContent = new StreamContent(stream);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
            form.Add(fileContent, "file", file.Name);
            if (!string.IsNullOrWhiteSpace(tags))
            {
                form.Add(new StringContent(tags), "tags");
            }

            return new HttpRequestMessage(HttpMethod.Post, path) { Content = form };
        }, withAuth: true, cancellationToken);
        await EnsureSuccess(response);

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
    }

    private static StringContent BuildJsonContent(object payload)
        => new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private void AddAuthHeader(HttpRequestMessage request, bool withAuth)
    {
        if (!withAuth || string.IsNullOrWhiteSpace(session.AccessToken))
        {
            return;
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
    }

    private async Task<HttpResponseMessage> SendWithAuthRetryAsync(
        Func<HttpRequestMessage> requestFactory,
        bool withAuth,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync(requestFactory, withAuth, cancellationToken);
        if (!withAuth || response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        response.Dispose();
        var refreshed = await TryRefreshSessionAsync(cancellationToken);
        if (!refreshed)
        {
            throw new UnauthorizedAccessException("Session expired. Please sign in again.");
        }

        return await SendAsync(requestFactory, withAuth, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(
        Func<HttpRequestMessage> requestFactory,
        bool withAuth,
        CancellationToken cancellationToken)
    {
        using var request = requestFactory();
        AddAuthHeader(request, withAuth);
        return await Client.SendAsync(request, cancellationToken);
    }

    private async Task<bool> TryRefreshSessionAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(session.RefreshToken) || session.RefreshTokenExpiresAt <= DateTimeOffset.UtcNow)
        {
            await SafeClearSessionAsync();
            return false;
        }

        var existingAccessToken = session.AccessToken;
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (!string.Equals(existingAccessToken, session.AccessToken, StringComparison.Ordinal))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(session.RefreshToken) || session.RefreshTokenExpiresAt <= DateTimeOffset.UtcNow)
            {
                await SafeClearSessionAsync();
                return false;
            }

            using var refreshRequest = new HttpRequestMessage(HttpMethod.Post, "/auth/refresh")
            {
                Content = BuildJsonContent(new { refreshToken = session.RefreshToken })
            };

            using var refreshResponse = await Client.SendAsync(refreshRequest, cancellationToken);
            if (!refreshResponse.IsSuccessStatusCode)
            {
                await SafeClearSessionAsync();
                return false;
            }

            var refreshed = await refreshResponse.Content.ReadFromJsonAsync<Normyx.Web.Models.AuthResponse>(JsonOptions, cancellationToken);
            if (refreshed is null || string.IsNullOrWhiteSpace(refreshed.AccessToken) || string.IsNullOrWhiteSpace(refreshed.RefreshToken))
            {
                await SafeClearSessionAsync();
                return false;
            }

            session.SetSession(session.TenantName, session.Email, refreshed.AccessToken, refreshed.RefreshToken, refreshed.RefreshTokenExpiresAt);
            await session.PersistAsync(jsRuntime);
            return true;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task SafeClearSessionAsync()
    {
        try
        {
            await session.ClearPersistedAsync(jsRuntime);
        }
        catch
        {
            session.Clear();
        }

        navigationManager.NavigateTo("/login");
    }

    private static async Task EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync();
        var errorMessage = TryExtractApiError(body);
        throw new HttpRequestException($"API call failed ({(int)response.StatusCode}): {errorMessage}");
    }

    private static string TryExtractApiError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "No error payload returned.";
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                var code = error.TryGetProperty("code", out var codeProp) ? codeProp.GetString() : null;
                var message = error.TryGetProperty("message", out var messageProp) ? messageProp.GetString() : null;
                var text = string.IsNullOrWhiteSpace(message) ? body : message;
                return string.IsNullOrWhiteSpace(code) ? text! : $"{code}: {text}";
            }
        }
        catch
        {
            // Non-JSON payload; return raw response body.
        }

        return body;
    }
}
