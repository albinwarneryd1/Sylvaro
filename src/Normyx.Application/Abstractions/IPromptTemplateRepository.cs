namespace Normyx.Application.Abstractions;

public interface IPromptTemplateRepository
{
    string GetSystemPrompt(string templateKey);
    string GetUserPrompt(string templateKey);
}
