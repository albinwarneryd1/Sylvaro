using Normyx.Application.Abstractions;

namespace Normyx.Infrastructure.AI;

public class LocalJsonCompletionProvider : IAiJsonCompletionProvider
{
    public Task<string> GenerateJsonAsync(string templateKey, string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("Local provider should use deterministic fallback in AiDraftService.");
    }
}
