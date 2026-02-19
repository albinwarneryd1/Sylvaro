namespace Normyx.Application.Abstractions;

public interface IWebhookPublisher
{
    Task<WebhookPublishResult> PublishAsync(string url, string? authHeader, object payload, CancellationToken cancellationToken = default);
}

public record WebhookPublishResult(bool Success, int StatusCode, string ResponseBody);
