using Microsoft.Extensions.Options;
using Normyx.Application.Abstractions;

namespace Normyx.Infrastructure.AI;

public class SwitchingJsonCompletionProvider(
    IOptions<AiProviderOptions> options,
    OpenAiCompatibleJsonCompletionProvider openAiProvider,
    LocalJsonCompletionProvider localProvider) : IAiJsonCompletionProvider
{
    private readonly AiProviderOptions _options = options.Value;

    public Task<string> GenerateJsonAsync(string templateKey, string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        if (_options.Mode.Equals("OpenAI", StringComparison.OrdinalIgnoreCase)
            || _options.Mode.Equals("AzureOpenAI", StringComparison.OrdinalIgnoreCase))
        {
            return openAiProvider.GenerateJsonAsync(templateKey, systemPrompt, userPrompt, cancellationToken);
        }

        return localProvider.GenerateJsonAsync(templateKey, systemPrompt, userPrompt, cancellationToken);
    }
}
