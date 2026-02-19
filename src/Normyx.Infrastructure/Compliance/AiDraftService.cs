using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Normyx.Application.Abstractions;
using Normyx.Application.Compliance;
using Normyx.Domain.Enums;
using Normyx.Infrastructure.AI;

namespace Normyx.Infrastructure.Compliance;

public class AiDraftService(
    IAiJsonCompletionProvider jsonCompletionProvider,
    IPromptTemplateRepository promptTemplates,
    IPiiRedactor piiRedactor,
    IOptions<AiProviderOptions> options,
    ILogger<AiDraftService> logger) : IAiDraftService
{
    private readonly AiProviderOptions _options = options.Value;

    public async Task<DraftActionPlanJson> GenerateActionPlanAsync(AssessmentSummary summary, IReadOnlyCollection<FindingDraft> findings, CancellationToken cancellationToken = default)
    {
        EnforceGuardrails(summary, findings);

        var fallback = CreateFallbackActionPlan(findings);
        var payloadJson = BuildModelInput(summary, findings);

        if (_options.Mode.Equals("Local", StringComparison.OrdinalIgnoreCase))
        {
            ValidateActionPlan(fallback);
            return fallback;
        }

        try
        {
            var rendered = RenderPrompt("action-plan", payloadJson);
            var output = await jsonCompletionProvider.GenerateJsonAsync("action-plan", rendered.SystemPrompt, rendered.UserPrompt, cancellationToken);
            var parsed = JsonSerializer.Deserialize<DraftActionPlanJson>(output, SerializerOptions);

            if (parsed is null)
            {
                throw new InvalidOperationException("AI action plan response was empty.");
            }

            ValidateActionPlan(parsed);
            return parsed;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Action plan generation failed, using deterministic fallback.");
            ValidateActionPlan(fallback);
            return fallback;
        }
    }

    public async Task<DraftDpiaJson> GenerateDpiaDraftAsync(AssessmentSummary summary, IReadOnlyCollection<FindingDraft> findings, CancellationToken cancellationToken = default)
    {
        EnforceGuardrails(summary, findings);

        var fallback = CreateFallbackDpia(summary, findings);
        var payloadJson = BuildModelInput(summary, findings);

        if (_options.Mode.Equals("Local", StringComparison.OrdinalIgnoreCase))
        {
            ValidateDpia(fallback);
            return fallback;
        }

        try
        {
            var rendered = RenderPrompt("dpia-draft", payloadJson);
            var output = await jsonCompletionProvider.GenerateJsonAsync("dpia-draft", rendered.SystemPrompt, rendered.UserPrompt, cancellationToken);
            var parsed = JsonSerializer.Deserialize<DraftDpiaJson>(output, SerializerOptions);

            if (parsed is null)
            {
                throw new InvalidOperationException("AI DPIA response was empty.");
            }

            ValidateDpia(parsed);
            return parsed;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "DPIA draft generation failed, using deterministic fallback.");
            ValidateDpia(fallback);
            return fallback;
        }
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private string BuildModelInput(AssessmentSummary summary, IReadOnlyCollection<FindingDraft> findings)
    {
        var modelInput = JsonSerializer.Serialize(new
        {
            summary,
            findings = findings.Select(x => new
            {
                x.Type,
                Severity = x.Severity.ToString(),
                x.Title,
                x.Description,
                x.RuleKeys,
                x.EvidenceSuggestions
            })
        });

        return _options.EnablePiiMasking ? piiRedactor.Redact(modelInput) : modelInput;
    }

    private (string SystemPrompt, string UserPrompt) RenderPrompt(string templateKey, string inputJson)
    {
        var systemPrompt = promptTemplates.GetSystemPrompt(templateKey);
        var userPrompt = promptTemplates.GetUserPrompt(templateKey)
            .Replace("{{INPUT_JSON}}", inputJson, StringComparison.Ordinal);

        return (systemPrompt, userPrompt);
    }

    private static void EnforceGuardrails(AssessmentSummary summary, IReadOnlyCollection<FindingDraft> findings)
    {
        var corpus = string.Join("\n", new[]
        {
            summary.Rationale,
            summary.AiActRiskClass,
            string.Join(" ", summary.GdprFlags),
            string.Join(" ", summary.Nis2Flags),
            string.Join(" ", findings.Select(x => x.Title + " " + x.Description))
        }).ToLowerInvariant();

        var blockedPhrases = new[]
        {
            "fake evidence",
            "fabricate evidence",
            "invent evidence",
            "forged evidence",
            "backdate evidence"
        };

        if (blockedPhrases.Any(corpus.Contains))
        {
            throw new InvalidOperationException("Guardrail blocked request: fake evidence generation is not allowed.");
        }
    }

    private static DraftActionPlanJson CreateFallbackActionPlan(IReadOnlyCollection<FindingDraft> findings)
    {
        var actions = findings
            .Select((finding, index) => new DraftActionItem(
                $"Address: {finding.Title}",
                finding.Description,
                finding.Severity switch
                {
                    FindingSeverity.Critical => "P0",
                    FindingSeverity.High => "P1",
                    FindingSeverity.Medium => "P2",
                    _ => "P3"
                },
                index % 2 == 0 ? "SecurityLead" : "ComplianceOfficer",
                $"Control objective met and evidence uploaded for {finding.Title}.",
                finding.EvidenceSuggestions.Length == 0 ? ["Policy evidence", "Control evidence"] : finding.EvidenceSuggestions))
            .ToArray();

        return new DraftActionPlanJson(actions);
    }

    private static DraftDpiaJson CreateFallbackDpia(AssessmentSummary summary, IReadOnlyCollection<FindingDraft> findings)
    {
        var sections = new List<DraftDpiaSection>
        {
            new("Processing context", ["AI system assessed as " + summary.AiActRiskClass, "GDPR flags: " + string.Join(", ", summary.GdprFlags)], ["Confirm purpose limitation wording"]),
            new("Risks", findings.Select(x => x.Title).Take(8).ToArray(), ["Legal review required for high-risk findings"]),
            new("Mitigations", ["Action plan approved", "Evidence links attached"], ["Complete supplier transfer assessment"])
        };

        return new DraftDpiaJson(sections);
    }

    private static void ValidateActionPlan(DraftActionPlanJson json)
    {
        if (json.Actions.Count == 0)
        {
            throw new InvalidOperationException("AI draft validation failed: actions[] required");
        }

        foreach (var action in json.Actions)
        {
            if (string.IsNullOrWhiteSpace(action.Priority) || string.IsNullOrWhiteSpace(action.OwnerRole) || string.IsNullOrWhiteSpace(action.AcceptanceCriteria))
            {
                throw new InvalidOperationException("AI draft validation failed: priority/ownerRole/acceptanceCriteria required");
            }
        }

        _ = JsonSerializer.Serialize(json);
    }

    private static void ValidateDpia(DraftDpiaJson json)
    {
        if (json.Sections.Count == 0 || json.Sections.Any(x => string.IsNullOrWhiteSpace(x.Title)))
        {
            throw new InvalidOperationException("AI DPIA validation failed: sections[] with title required");
        }

        _ = JsonSerializer.Serialize(json);
    }
}
