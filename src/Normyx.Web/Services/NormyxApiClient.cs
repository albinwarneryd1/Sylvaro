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
        var refreshResult = await TryRefreshSessionAsync(cancellationToken);
        if (refreshResult == RefreshSessionResult.Refreshed)
        {
            return await SendAsync(requestFactory, withAuth, cancellationToken);
        }

        if (refreshResult == RefreshSessionResult.Expired)
        {
            throw new UnauthorizedAccessException("Session expired. Please sign in again.");
        }

        throw new HttpRequestException("Session refresh failed due to a temporary service issue. Retry in a moment.");
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

    private async Task<RefreshSessionResult> TryRefreshSessionAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(session.RefreshToken) || session.RefreshTokenExpiresAt <= DateTimeOffset.UtcNow)
        {
            await SafeClearSessionAsync();
            return RefreshSessionResult.Expired;
        }

        var existingAccessToken = session.AccessToken;
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (!string.Equals(existingAccessToken, session.AccessToken, StringComparison.Ordinal))
            {
                return RefreshSessionResult.Refreshed;
            }

            if (string.IsNullOrWhiteSpace(session.RefreshToken) || session.RefreshTokenExpiresAt <= DateTimeOffset.UtcNow)
            {
                await SafeClearSessionAsync();
                return RefreshSessionResult.Expired;
            }

            try
            {
                using var refreshRequest = new HttpRequestMessage(HttpMethod.Post, "/auth/refresh")
                {
                    Content = BuildJsonContent(new { refreshToken = session.RefreshToken })
                };

                using var refreshResponse = await Client.SendAsync(refreshRequest, cancellationToken);
                if (!refreshResponse.IsSuccessStatusCode)
                {
                    if (refreshResponse.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    {
                        await SafeClearSessionAsync();
                        return RefreshSessionResult.Expired;
                    }

                    return RefreshSessionResult.TransientFailure;
                }

                var refreshed = await refreshResponse.Content.ReadFromJsonAsync<Normyx.Web.Models.AuthResponse>(JsonOptions, cancellationToken);
                if (refreshed is null || string.IsNullOrWhiteSpace(refreshed.AccessToken) || string.IsNullOrWhiteSpace(refreshed.RefreshToken))
                {
                    await SafeClearSessionAsync();
                    return RefreshSessionResult.Expired;
                }

                session.SetSession(session.TenantName, session.Email, refreshed.AccessToken, refreshed.RefreshToken, refreshed.RefreshTokenExpiresAt);
                await session.PersistAsync(jsRuntime);
                return RefreshSessionResult.Refreshed;
            }
            catch (HttpRequestException)
            {
                return RefreshSessionResult.TransientFailure;
            }
            catch (TaskCanceledException)
            {
                return RefreshSessionResult.TransientFailure;
            }
            catch (JsonException)
            {
                return RefreshSessionResult.TransientFailure;
            }
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

        TryNavigateToLogin();
    }

    private void TryNavigateToLogin()
    {
        try
        {
            navigationManager.NavigateTo("/login", replace: true);
        }
        catch (NavigationException)
        {
            // In server-side redirect scenarios Blazor can throw NavigationException intentionally.
        }
    }

    private static async Task EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync();
        var parsed = TryExtractApiError(body);
        var errorMessage = string.IsNullOrWhiteSpace(parsed.CorrelationId)
            ? parsed.Message
            : $"{parsed.Message} (corr:{parsed.CorrelationId})";
        throw new HttpRequestException($"API call failed ({(int)response.StatusCode}): {errorMessage}");
    }

    private static ApiErrorParse TryExtractApiError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return new ApiErrorParse("No error payload returned.", null);
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var correlationId = document.RootElement.TryGetProperty("correlationId", out var correlationProp)
                ? correlationProp.GetString()
                : null;

            if (document.RootElement.TryGetProperty("error", out var error))
            {
                var code = error.TryGetProperty("code", out var codeProp) ? codeProp.GetString() : null;
                var message = error.TryGetProperty("message", out var messageProp) ? messageProp.GetString() : null;
                var text = string.IsNullOrWhiteSpace(message) ? body : message;
                var formatted = string.IsNullOrWhiteSpace(code) ? text! : $"{code}: {text}";
                return new ApiErrorParse(formatted, correlationId);
            }
        }
        catch
        {
            // Non-JSON payload; return raw response body.
        }

        return new ApiErrorParse(body, null);
    }

    private sealed record ApiErrorParse(string Message, string? CorrelationId);

    private enum RefreshSessionResult
    {
        Refreshed,
        Expired,
        TransientFailure
    }
}
