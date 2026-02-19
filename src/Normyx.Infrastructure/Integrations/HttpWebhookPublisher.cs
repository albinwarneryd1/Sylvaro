using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Normyx.Application.Abstractions;

namespace Normyx.Infrastructure.Integrations;

public class HttpWebhookPublisher(IHttpClientFactory factory) : IWebhookPublisher
{
    public async Task<WebhookPublishResult> PublishAsync(string url, string? authHeader, object payload, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

        if (!string.IsNullOrWhiteSpace(authHeader))
        {
            if (AuthenticationHeaderValue.TryParse(authHeader, out var authValue))
            {
                request.Headers.Authorization = authValue;
            }
            else
            {
                request.Headers.TryAddWithoutValidation("Authorization", authHeader);
            }
        }

        var client = factory.CreateClient("WebhookPublisher");
        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        return new WebhookPublishResult(response.IsSuccessStatusCode, (int)response.StatusCode, body);
    }
}
