using Normyx.Application.Abstractions;

namespace Normyx.Infrastructure.AI;

public class FilePromptTemplateRepository : IPromptTemplateRepository
{
    private readonly string _promptRoot;

    public FilePromptTemplateRepository()
    {
        _promptRoot = ResolvePromptRoot();
    }

    public string GetSystemPrompt(string templateKey)
        => ReadTemplate(templateKey, "system", DefaultSystemPrompt(templateKey));

    public string GetUserPrompt(string templateKey)
        => ReadTemplate(templateKey, "user", "{{INPUT_JSON}}");

    private string ReadTemplate(string templateKey, string templateType, string fallback)
    {
        var file = Path.Combine(_promptRoot, $"{templateKey}.{templateType}.txt");
        if (!File.Exists(file))
        {
            return fallback;
        }

        return File.ReadAllText(file);
    }

    private static string ResolvePromptRoot()
    {
        var direct = Path.Combine(AppContext.BaseDirectory, "prompts");
        if (Directory.Exists(direct))
        {
            return direct;
        }

        var local = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "prompts"));
        return Directory.Exists(local) ? local : direct;
    }

    private static string DefaultSystemPrompt(string templateKey)
        => templateKey switch
        {
            "action-plan" => "You are a compliance assistant. Return strict JSON with top-level 'actions'.",
            "dpia-draft" => "You are a compliance assistant. Return strict JSON with top-level 'sections'.",
            _ => "Return strict JSON only."
        };
}
