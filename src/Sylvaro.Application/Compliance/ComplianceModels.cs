using Normyx.Domain.Enums;

namespace Normyx.Application.Compliance;

public record DraftActionItem(
    string Title,
    string Description,
    string Priority,
    string OwnerRole,
    string AcceptanceCriteria,
    string[] EvidenceNeeded);

public record DraftActionPlanJson(IReadOnlyCollection<DraftActionItem> Actions);

public record DraftDpiaSection(string Title, string[] Claims, string[] Uncertainties);
public record DraftDpiaJson(IReadOnlyCollection<DraftDpiaSection> Sections);

public record FindingDraft(
    string Type,
    FindingSeverity Severity,
    string Title,
    string Description,
    Guid[] AffectedComponentIds,
    string[] RuleKeys,
    string[] EvidenceSuggestions);

public record AssessmentSummary(
    string AiActRiskClass,
    string[] TriggeredRuleKeys,
    string[] GdprFlags,
    bool DpiaSuggested,
    string[] Nis2Flags,
    Dictionary<Guid, string> ComponentRiskMap,
    int ComplianceScore,
    string[] PolicyPackVersionRefs,
    string Rationale);

public record AssessmentRunResult(
    Guid AssessmentId,
    string AiActRiskClass,
    int ComplianceScore,
    int FindingsCount,
    int ActionsCount);
