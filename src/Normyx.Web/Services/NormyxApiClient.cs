using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Forms;

namespace Normyx.Web.Services;

public class NormyxApiClient(IHttpClientFactory factory, AuthSession session)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private HttpClient Client => factory.CreateClient("NormyxApi");

    public async Task<T?> GetAsync<T>(string path, bool withAuth = true, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        AddAuthHeader(request, withAuth);

        using var response = await Client.SendAsync(request, cancellationToken);
        await EnsureSuccess(response);

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
    }

    public async Task<T?> PostAsync<T>(string path, object payload, bool withAuth = true, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = BuildJsonContent(payload)
        };
        AddAuthHeader(request, withAuth);

        using var response = await Client.SendAsync(request, cancellationToken);
        await EnsureSuccess(response);

        if (response.Content.Headers.ContentLength is 0)
        {
            return default;
        }

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
    }

    public async Task PostAsync(string path, object payload, bool withAuth = true, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = BuildJsonContent(payload)
        };
        AddAuthHeader(request, withAuth);

        using var response = await Client.SendAsync(request, cancellationToken);
        await EnsureSuccess(response);
    }

    public async Task PutAsync(string path, object payload, bool withAuth = true, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, path)
        {
            Content = BuildJsonContent(payload)
        };
        AddAuthHeader(request, withAuth);

        using var response = await Client.SendAsync(request, cancellationToken);
        await EnsureSuccess(response);
    }

    public async Task DeleteAsync(string path, bool withAuth = true, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, path);
        AddAuthHeader(request, withAuth);

        using var response = await Client.SendAsync(request, cancellationToken);
        await EnsureSuccess(response);
    }

    public async Task<T?> UploadFileAsync<T>(string path, IBrowserFile file, string? tags = null, CancellationToken cancellationToken = default)
    {
        using var form = new MultipartFormDataContent();
        await using var stream = file.OpenReadStream(20 * 1024 * 1024, cancellationToken);
        var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
        form.Add(fileContent, "file", file.Name);
        if (!string.IsNullOrWhiteSpace(tags))
        {
            form.Add(new StringContent(tags), "tags");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = form };
        AddAuthHeader(request, true);

        using var response = await Client.SendAsync(request, cancellationToken);
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

    private static async Task EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync();
        throw new HttpRequestException($"API call failed ({(int)response.StatusCode}): {body}");
    }
}
