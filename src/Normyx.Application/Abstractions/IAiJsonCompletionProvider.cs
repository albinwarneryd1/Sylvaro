namespace Normyx.Application.Abstractions;

public interface IAiJsonCompletionProvider
{
    Task<string> GenerateJsonAsync(
        string templateKey,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default);
}
