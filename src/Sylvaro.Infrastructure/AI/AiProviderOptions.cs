namespace Normyx.Infrastructure.AI;

public class AiProviderOptions
{
    public const string SectionName = "AiProvider";

    public string Mode { get; set; } = "Local";
    public string BaseUrl { get; set; } = "https://api.openai.com/v1/chat/completions";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-4.1-mini";
    public int MaxTokens { get; set; } = 1200;
    public bool EnablePiiMasking { get; set; } = true;
}
