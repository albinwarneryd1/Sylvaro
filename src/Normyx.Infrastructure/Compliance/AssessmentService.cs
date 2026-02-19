using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Normyx.Application.Abstractions;
using Normyx.Application.Compliance;
using Normyx.Infrastructure.Persistence;
using Normyx.Domain.Entities;
using Normyx.Domain.Enums;

namespace Normyx.Infrastructure.Compliance;

public class AssessmentService(
    NormyxDbContext dbContext,
    IAiDraftService aiDraftService,
    PolicyEngine policyEngine,
    IAssessmentExecutionGuard executionGuard) : IAssessmentService
{
    public async Task<AssessmentRunResult> RunAssessmentAsync(Guid tenantId, Guid versionId, Guid ranByUserId, CancellationToken cancellationToken = default)
    {
        await using var executionHandle = await executionGuard.AcquireAsync(tenantId, versionId, cancellationToken);

        var version = await dbContext.AiSystemVersions
            .Include(x => x.AiSystem)
            .FirstOrDefaultAsync(x => x.Id == versionId && x.AiSystem.TenantId == tenantId, cancellationToken);

        if (version is null)
        {
            throw new InvalidOperationException("Version not found for tenant");
        }

        var components = await dbContext.Components.Where(x => x.AiSystemVersionId == versionId).ToListAsync(cancellationToken);
        var inventory = await dbContext.DataInventoryItems.Where(x => x.AiSystemVersionId == versionId).ToListAsync(cancellationToken);
        var vendors = await dbContext.Vendors.Where(x => x.AiSystemVersionId == versionId).ToListAsync(cancellationToken);
        var questionnaire = await dbContext.ComplianceQuestionnaires.FirstOrDefaultAsync(x => x.AiSystemVersionId == versionId, cancellationToken);

        var description = version.AiSystem.Description;
        var hasPersonalData = inventory.Any(x => x.ContainsPersonalData);
        var hasSpecialCategory = inventory.Any(x => x.SpecialCategory);
        var transferOutsideEu = inventory.Any(x => x.TransferOutsideEu) || vendors.Any(v => v.Region.Contains("US", StringComparison.OrdinalIgnoreCase));
        var missingLawfulBasis = inventory.Any(x => string.IsNullOrWhiteSpace(x.LawfulBasis));
        var longRetention = inventory.Any(x => x.RetentionDays > 1825);

        var lowerDesc = description.ToLowerInvariant();
        var hasProhibitedPattern = lowerDesc.Contains("social scoring") || lowerDesc.Contains("biometric surveillance") || lowerDesc.Contains("emotion recognition");
        var hasHighRiskDomain = lowerDesc.Contains("credit") || lowerDesc.Contains("loan") || lowerDesc.Contains("employment") || lowerDesc.Contains("health") || lowerDesc.Contains("critical infrastructure");
        var hasProfilingPattern = lowerDesc.Contains("profil") || lowerDesc.Contains("automated decision");

        var answers = ParseQuestionnaire(questionnaire?.AnswersJson);
        var autoDecision = TryGetBool(answers, "automatedDecisionMaking") || hasProfilingPattern;
        var criticalSector = TryGetBool(answers, "criticalSector") || hasHighRiskDomain;

        var aiActClass = hasProhibitedPattern
            ? "prohibited"
            : (hasHighRiskDomain || hasSpecialCategory || autoDecision)
                ? "high-risk"
                : (hasPersonalData || lowerDesc.Contains("chatbot") || lowerDesc.Contains("llm"))
                    ? "limited"
                    : "minimal";

        var gdprFlags = new List<string>();
        if (hasPersonalData) gdprFlags.Add("Personal data processed");
        if (hasSpecialCategory) gdprFlags.Add("Special category data processed");
        if (transferOutsideEu) gdprFlags.Add("Transfer outside EU detected");
        if (missingLawfulBasis) gdprFlags.Add("Missing lawful basis");
        if (longRetention) gdprFlags.Add("Retention period exceeds baseline");
        if (autoDecision) gdprFlags.Add("Automated decision/profiling pattern");

        var nis2Flags = new List<string>();
        if (criticalSector) nis2Flags.Add("Critical sector relevance");
        if (vendors.Count > 0) nis2Flags.Add("Supplier risk and third-party controls required");
        if (components.Any(c => c.IsExternal)) nis2Flags.Add("External-facing components require stronger monitoring");

        var componentRisk = components.ToDictionary(
            c => c.Id,
            c => c.DataSensitivityLevel.Equals("High", StringComparison.OrdinalIgnoreCase)
                ? "High"
                : c.IsExternal ? "Medium" : "Low");

        var facts = new Dictionary<string, object?>
        {
            ["inventory.personal_data"] = hasPersonalData,
            ["inventory.special_category"] = hasSpecialCategory,
            ["inventory.transfer_outside_eu"] = transferOutsideEu,
            ["inventory.missing_lawful_basis"] = missingLawfulBasis,
            ["inventory.max_retention_days"] = inventory.Count == 0 ? 0 : inventory.Max(x => x.RetentionDays),
            ["system.prohibited_pattern"] = hasProhibitedPattern,
            ["system.high_risk_domain"] = hasHighRiskDomain,
            ["questionnaire.automated_decision"] = autoDecision,
            ["questionnaire.critical_sector"] = criticalSector
        };

        var policyRoot = Path.Combine(AppContext.BaseDirectory, "policy-packs");
        if (!Directory.Exists(policyRoot))
        {
            policyRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "policy-packs"));
        }

        var policyMatches = policyEngine.Evaluate(policyRoot, facts);

        var findings = new List<FindingDraft>();
        findings.AddRange(policyMatches.Select(match => new FindingDraft(
            "PolicyRule",
            match.Severity,
            match.RuleKey,
            match.Description,
            [],
            [match.RuleKey],
            ["Linked evidence excerpt"])));

        if (hasProhibitedPattern)
        {
            findings.Add(new FindingDraft("AI_ACT", FindingSeverity.Critical, "Prohibited AI use pattern", "System description contains prohibited AI use signal.", [], ["AIACT-PROHIBITED"], ["Use-case policy"]));
        }
        else if (aiActClass == "high-risk")
        {
            findings.Add(new FindingDraft("AI_ACT", FindingSeverity.High, "High-risk AI classification", "System appears in high-risk use domain.", [], ["AIACT-HIGHRISK"], ["Risk management process", "Conformity assessment process"]));
        }

        foreach (var flag in gdprFlags)
        {
            findings.Add(new FindingDraft("GDPR", flag.Contains("Missing") ? FindingSeverity.High : FindingSeverity.Medium, flag, flag, [], ["GDPR-TRIGGER"], ["RoPA", "DPIA section"]));
        }

        foreach (var flag in nis2Flags)
        {
            findings.Add(new FindingDraft("NIS2", FindingSeverity.Medium, flag, flag, [], ["NIS2-BASELINE"], ["Incident response plan", "Monitoring evidence"]));
        }

        var complianceScore = Math.Max(0, 100 - findings.Sum(x => x.Severity switch
        {
            FindingSeverity.Critical => 20,
            FindingSeverity.High => 12,
            FindingSeverity.Medium => 6,
            _ => 3
        }));

        var selectedPolicyPackRefs = await dbContext.TenantPolicyPackSelections
            .Where(x => x.TenantId == tenantId && x.IsEnabled)
            .Join(dbContext.PolicyPacks, s => s.PolicyPackId, p => p.Id, (_, p) => p.Name + " " + p.Version)
            .Distinct()
            .ToArrayAsync(cancellationToken);

        if (selectedPolicyPackRefs.Length == 0)
        {
            selectedPolicyPackRefs = await dbContext.PolicyPacks
                .Select(x => x.Name + " " + x.Version)
                .Distinct()
                .ToArrayAsync(cancellationToken);
        }

        var summary = new AssessmentSummary(
            aiActClass,
            findings.SelectMany(x => x.RuleKeys).Distinct().ToArray(),
            gdprFlags.ToArray(),
            hasSpecialCategory || (autoDecision && hasPersonalData),
            nis2Flags.ToArray(),
            componentRisk,
            complianceScore,
            selectedPolicyPackRefs,
            "Deterministic policy evaluation + structured AI draft generation");

        var actionPlan = await aiDraftService.GenerateActionPlanAsync(summary, findings, cancellationToken);
        var dpiaDraft = await aiDraftService.GenerateDpiaDraftAsync(summary, findings, cancellationToken);

        var assessment = new Assessment
        {
            Id = Guid.NewGuid(),
            AiSystemVersionId = versionId,
            RanByUserId = ranByUserId,
            RanAt = DateTimeOffset.UtcNow,
            LlmProvider = "StructuredDraftGenerator",
            PolicyPackVersionRefs = summary.PolicyPackVersionRefs,
            SummaryJson = JsonSerializer.Serialize(new { summary, dpiaDraft }),
            RiskScoresJson = JsonSerializer.Serialize(new
            {
                aiActClass = summary.AiActRiskClass,
                gdprScore = Math.Max(0, 100 - summary.GdprFlags.Length * 10),
                nis2Score = Math.Max(0, 100 - summary.Nis2Flags.Length * 10),
                totalScore = summary.ComplianceScore
            })
        };

        dbContext.Assessments.Add(assessment);
        await dbContext.SaveChangesAsync(cancellationToken);

        var findingEntities = findings.Select(finding => new Finding
        {
            Id = Guid.NewGuid(),
            AssessmentId = assessment.Id,
            Type = finding.Type,
            Severity = finding.Severity,
            Title = finding.Title,
            Description = finding.Description,
            AffectedComponentIds = finding.AffectedComponentIds,
            EvidenceLinks = []
        }).ToList();

        dbContext.Findings.AddRange(findingEntities);

        var actionEntities = actionPlan.Actions.Select((action, idx) => new ActionItem
        {
            Id = Guid.NewGuid(),
            AiSystemVersionId = versionId,
            SourceFindingId = idx < findingEntities.Count ? findingEntities[idx].Id : null,
            Title = action.Title,
            Description = action.Description,
            Priority = action.Priority,
            OwnerRole = action.OwnerRole,
            Status = ActionStatus.New,
            AcceptanceCriteria = action.AcceptanceCriteria
        }).ToList();

        dbContext.ActionItems.AddRange(actionEntities);

        var controlKeys = policyMatches.SelectMany(x => x.OutputControlKeys).Distinct().ToArray();
        var controls = controlKeys.Select(controlKey => new ControlInstance
        {
            Id = Guid.NewGuid(),
            AiSystemVersionId = versionId,
            ControlKey = controlKey,
            Status = ControlStatus.PendingReview,
            Notes = "Generated from policy engine"
        }).ToList();

        dbContext.ControlInstances.AddRange(controls);

        await dbContext.SaveChangesAsync(cancellationToken);

        return new AssessmentRunResult(assessment.Id, summary.AiActRiskClass, summary.ComplianceScore, findingEntities.Count, actionEntities.Count);
    }

    private static Dictionary<string, string> ParseQuestionnaire(string? answersJson)
    {
        if (string.IsNullOrWhiteSpace(answersJson))
        {
            return new Dictionary<string, string>();
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(answersJson) ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    private static bool TryGetBool(Dictionary<string, string> values, string key)
        => values.TryGetValue(key, out var raw) && bool.TryParse(raw, out var parsed) && parsed;
}
