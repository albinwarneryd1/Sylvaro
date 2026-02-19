using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Normyx.Application.Abstractions;

namespace Normyx.Infrastructure.AI;

public class OpenAiCompatibleJsonCompletionProvider(
    IHttpClientFactory factory,
    IOptions<AiProviderOptions> options,
    ILogger<OpenAiCompatibleJsonCompletionProvider> logger) : IAiJsonCompletionProvider
{
    private readonly AiProviderOptions _options = options.Value;

    public async Task<string> GenerateJsonAsync(string templateKey, string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        if (!_options.Mode.Equals("OpenAI", StringComparison.OrdinalIgnoreCase)
            && !_options.Mode.Equals("AzureOpenAI", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Provider not enabled.");
        }

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("AI provider API key is missing.");
        }

        var payload = new
        {
            model = _options.Model,
            max_tokens = _options.MaxTokens,
            temperature = 0.1,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.BaseUrl)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        var client = factory.CreateClient("AiProvider");
        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("AI provider call failed with status {StatusCode}: {Body}", response.StatusCode, body);
            throw new HttpRequestException($"AI provider call failed: {(int)response.StatusCode}");
        }

        using var doc = JsonDocument.Parse(body);
        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("AI provider returned empty content.");
        }

        return content;
    }
}
