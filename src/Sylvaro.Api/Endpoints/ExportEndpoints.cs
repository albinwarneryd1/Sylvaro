using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sylvaro.Api.Utilities;
using Sylvaro.Application.Abstractions;
using Sylvaro.Application.Security;
using Sylvaro.Domain.Entities;
using Sylvaro.Domain.Enums;
using Sylvaro.Infrastructure.Persistence;

namespace Sylvaro.Api.Endpoints;

public static class ExportEndpoints
{
    public static IEndpointRouteBuilder MapExportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/exports").WithTags("Exports").RequireAuthorization().WithRequestValidation();

        group.MapPost("/versions/{versionId:guid}/generate", GenerateExportAsync)
            .RequireAuthorization(new AuthorizeAttribute { Roles = $"{RoleNames.Admin},{RoleNames.ComplianceOfficer}" });
        group.MapGet("/versions/{versionId:guid}", ListExportsAsync);
        group.MapGet("/{artifactId:guid}/download", DownloadExportAsync);

        return app;
    }

    public record GenerateExportRequest(
        [property: Required, StringLength(80, MinimumLength = 2)] string ExportType,
        [property: Required, RegularExpression("^(pdf|json)$")] string Format = "pdf",
        bool SendWebhook = false);

    private static async Task<IResult> GenerateExportAsync(
        [FromRoute] Guid versionId,
        [FromBody] GenerateExportRequest request,
        SylvaroDbContext dbContext,
        ICurrentUserContext currentUser,
        IExportService exportService,
        IObjectStorage objectStorage,
        IWebhookPublisher webhookPublisher)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);
        var userId = TenantContext.RequireUserId(currentUser);

        var version = await dbContext.AiSystemVersions
            .Include(x => x.AiSystem)
            .FirstOrDefaultAsync(x => x.Id == versionId && x.AiSystem.TenantId == tenantId);

        if (version is null)
        {
            return Results.NotFound();
        }

        var latestAssessment = await dbContext.Assessments
            .Where(x => x.AiSystemVersionId == versionId)
            .OrderByDescending(x => x.RanAt)
            .FirstOrDefaultAsync();

        var actions = await dbContext.ActionItems
            .Where(x => x.AiSystemVersionId == versionId)
            .OrderBy(x => x.Priority)
            .ToListAsync();

        var controls = await dbContext.ControlInstances
            .Where(x => x.AiSystemVersionId == versionId)
            .ToListAsync();

        var findings = latestAssessment is null
            ? new List<Finding>()
            : await dbContext.Findings.Where(x => x.AssessmentId == latestAssessment.Id).ToListAsync();

        var linkedEvidence = await dbContext.EvidenceLinks
            .Where(x => x.EvidenceExcerpt.Document.TenantId == tenantId)
            .CountAsync();

        var controlIds = controls.Select(x => x.Id).ToList();
        var findingIds = findings.Select(x => x.Id).ToList();

        var linkedControlEvidence = controlIds.Count == 0
            ? 0
            : await dbContext.EvidenceLinks
                .Where(x => x.TargetType == "ControlInstance" && controlIds.Contains(x.TargetId) && x.EvidenceExcerpt.Document.TenantId == tenantId)
                .CountAsync();

        var linkedFindingEvidence = findingIds.Count == 0
            ? 0
            : await dbContext.EvidenceLinks
                .Where(x => x.TargetType == "Finding" && findingIds.Contains(x.TargetId) && x.EvidenceExcerpt.Document.TenantId == tenantId)
                .CountAsync();

        var totalDocuments = await dbContext.Documents.CountAsync(x => x.TenantId == tenantId);
        var freshDocuments = await dbContext.Documents.CountAsync(x => x.TenantId == tenantId && x.UploadedAt >= DateTimeOffset.UtcNow.AddDays(-180));

        var controlCoveragePercent = controls.Count == 0 ? 0 : (int)Math.Round(100d * linkedControlEvidence / controls.Count);
        var findingEvidencePercent = findings.Count == 0 ? 100 : (int)Math.Round(100d * linkedFindingEvidence / findings.Count);
        var evidenceFreshnessScore = totalDocuments == 0 ? 40 : (int)Math.Round(100d * freshDocuments / totalDocuments);
        var openObligations = actions.Count(x => x.Status is not ActionStatus.Done and not ActionStatus.AcceptedRisk);
        var criticalDeficiencies = actions.Count(x => x.Priority.Equals("Critical", StringComparison.OrdinalIgnoreCase));
        var complianceScore = latestAssessment is null ? 0 : ExtractTotalScore(latestAssessment.RiskScoresJson);

        var evidenceIntegrityScore = (int)Math.Round(
            evidenceFreshnessScore * 0.30 +
            findingEvidencePercent * 0.35 +
            controlCoveragePercent * 0.35);

        var governanceSummary = new GovernanceSummary(
            complianceScore,
            openObligations,
            criticalDeficiencies,
            controlCoveragePercent,
            evidenceIntegrityScore,
            evidenceFreshnessScore,
            findingEvidencePercent,
            linkedEvidence);

        var riskHeatRows = BuildRiskHeatRows(actions, findings);

        var exportPayload = new
        {
            request.ExportType,
            version = new
            {
                versionId,
                version.VersionNumber,
                version.ChangeSummary,
                systemName = version.AiSystem.Name
            },
            policyPackVersions = latestAssessment?.PolicyPackVersionRefs ?? [],
            signOffs = actions.Where(x => x.ApprovedAt.HasValue).Select(x => new { x.Id, x.Title, x.ApprovedBy, x.ApprovedAt }),
            governanceSummary,
            riskHeatmapSnapshot = riskHeatRows,
            latestAssessment = latestAssessment is null
                ? null
                : new
                {
                    latestAssessment.Id,
                    latestAssessment.RanAt,
                    latestAssessment.RiskScoresJson,
                    latestAssessment.SummaryJson
                },
            actions = actions.Select(x => new
            {
                x.Id,
                x.Title,
                x.Description,
                x.Priority,
                x.OwnerRole,
                status = x.Status.ToString(),
                x.AcceptanceCriteria,
                x.ApprovedBy,
                x.ApprovedAt
            }),
            controls = controls.Select(x => new
            {
                x.Id,
                x.ControlKey,
                status = x.Status.ToString(),
                x.Notes,
                x.ApprovedByUserId,
                x.ApprovedAt
            })
        };

        byte[] outputBytes;
        string outputContentType;
        string extension;

        if (request.Format.Equals("json", StringComparison.OrdinalIgnoreCase))
        {
            outputBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(exportPayload, new JsonSerializerOptions { WriteIndented = true }));
            outputContentType = "application/json";
            extension = "json";
        }
        else
        {
            var lines = BuildPdfLines(request.ExportType, version, latestAssessment, actions, controls, governanceSummary, riskHeatRows);
            outputBytes = await exportService.GeneratePdfAsync($"Sylvaro {request.ExportType}", lines);
            outputContentType = "application/pdf";
            extension = "pdf";
        }

        await using var stream = new MemoryStream(outputBytes);
        var storageRef = await objectStorage.SaveAsync(
            $"{request.ExportType}-{version.AiSystem.Name}-v{version.VersionNumber}.{extension}",
            outputContentType,
            stream);

        var artifact = new ExportArtifact
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AiSystemVersionId = versionId,
            ExportType = request.ExportType,
            MimeType = outputContentType,
            StorageRef = storageRef,
            CreatedByUserId = userId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        dbContext.ExportArtifacts.Add(artifact);
        await dbContext.SaveChangesAsync();

        WebhookPublishResult[] webhookResults = [];
        if (request.SendWebhook)
        {
            webhookResults = await SendToEnabledWebhooksAsync(dbContext, tenantId, webhookPublisher, new
            {
                eventType = "sylvaro.export.generated",
                artifactId = artifact.Id,
                artifact.ExportType,
                artifact.MimeType,
                artifact.CreatedAt,
                export = exportPayload
            });
        }

        return Results.Ok(new
        {
            artifact.Id,
            artifact.ExportType,
            artifact.MimeType,
            artifact.CreatedAt,
            webhookResults
        });
    }

    private static async Task<IResult> ListExportsAsync([FromRoute] Guid versionId, SylvaroDbContext dbContext, ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);

        var exports = await dbContext.ExportArtifacts
            .Where(x => x.AiSystemVersionId == versionId && x.TenantId == tenantId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new
            {
                x.Id,
                x.ExportType,
                x.MimeType,
                x.CreatedAt,
                x.CreatedByUserId
            })
            .ToListAsync();

        return Results.Ok(exports);
    }

    private static async Task<IResult> DownloadExportAsync([FromRoute] Guid artifactId, SylvaroDbContext dbContext, ICurrentUserContext currentUser, IObjectStorage objectStorage)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);
        var artifact = await dbContext.ExportArtifacts.FirstOrDefaultAsync(x => x.Id == artifactId && x.TenantId == tenantId);

        if (artifact is null)
        {
            return Results.NotFound();
        }

        var (stream, contentType) = await objectStorage.OpenReadAsync(artifact.StorageRef);
        var fileExtension = artifact.MimeType.Equals("application/json", StringComparison.OrdinalIgnoreCase) ? "json" : "pdf";
        var fileName = $"{artifact.ExportType}-{artifact.Id}.{fileExtension}";
        return Results.File(stream, string.IsNullOrWhiteSpace(contentType) ? artifact.MimeType : contentType, fileName);
    }

    private static List<string> BuildPdfLines(
        string exportType,
        AiSystemVersion version,
        Assessment? latestAssessment,
        List<ActionItem> actions,
        List<ControlInstance> controls,
        GovernanceSummary governance,
        IReadOnlyList<RiskHeatRow> riskHeatRows)
    {
        var lines = new List<string>
        {
            "# Governance Summary",
            $"Export Type: {exportType}",
            $"AI System: {version.AiSystem.Name}",
            $"Version: {version.VersionNumber}",
            $"Generated: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC",
            $"Compliance Score: {governance.ComplianceScore}",
            $"Outstanding Obligations: {governance.OpenObligations}",
            $"Critical Deficiencies: {governance.CriticalDeficiencies}",
            $"Control Coverage: {governance.ControlCoveragePercent}%",
            $"Evidence Integrity Score: {governance.EvidenceIntegrityScore}%",
            "",
            "## Evidence Posture",
            $"- Evidence freshness: {governance.EvidenceFreshnessScore}%",
            $"- Finding evidence linkage: {governance.FindingEvidencePercent}%",
            $"- Linked evidence references: {governance.LinkedEvidenceCount}",
            "",
            "## Risk Heatmap Snapshot",
            "| Severity | Findings | Actions |",
            "| --- | ---: | ---: |"
        };

        lines.AddRange(riskHeatRows.Select(row => $"| {row.Severity} | {row.FindingCount} | {row.ActionCount} |"));

        if (latestAssessment is not null)
        {
            lines.Add("");
            lines.Add("## Assessment Context");
            lines.Add($"- Assessment run: {latestAssessment.RanAt:yyyy-MM-dd HH:mm:ss} UTC");
            lines.Add("- Policy pack versions: " + string.Join(", ", latestAssessment.PolicyPackVersionRefs));
        }

        lines.Add("");
        lines.Add("## Remediation Plan");
        lines.AddRange(actions.Select(x => $"- [{x.Priority}] {x.Title} ({x.Status}) owner={x.OwnerRole} signoff={x.ApprovedAt?.ToString("u") ?? "pending"}"));

        lines.Add("");
        lines.Add("## Control Register");
        lines.AddRange(controls.Select(x => $"- {x.ControlKey}: {x.Status} signoff={x.ApprovedAt?.ToString("u") ?? "pending"}"));

        return lines;
    }

    private static IReadOnlyList<RiskHeatRow> BuildRiskHeatRows(List<ActionItem> actions, List<Finding> findings)
    {
        var severities = new[] { "Critical", "High", "Medium", "Low" };
        return severities
            .Select(severity => new RiskHeatRow(
                severity,
                findings.Count(f => f.Severity.ToString().Equals(severity, StringComparison.OrdinalIgnoreCase)),
                actions.Count(a => a.Priority.Equals(severity, StringComparison.OrdinalIgnoreCase))))
            .ToList();
    }

    private static int ExtractTotalScore(string riskScoresJson)
    {
        if (string.IsNullOrWhiteSpace(riskScoresJson))
        {
            return 0;
        }

        var marker = "\"totalScore\":";
        var index = riskScoresJson.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return 0;
        }

        var start = index + marker.Length;
        var end = riskScoresJson.IndexOfAny([',', '}'], start);
        var span = end > start ? riskScoresJson[start..end] : riskScoresJson[start..];

        return int.TryParse(span, out var score) ? score : 0;
    }

    private static async Task<WebhookPublishResult[]> SendToEnabledWebhooksAsync(
        SylvaroDbContext dbContext,
        Guid tenantId,
        IWebhookPublisher webhookPublisher,
        object payload)
    {
        var integrations = await dbContext.TenantIntegrations
            .Where(x => x.TenantId == tenantId && x.IsEnabled && !string.IsNullOrWhiteSpace(x.WebhookUrl))
            .ToListAsync();

        var results = new List<WebhookPublishResult>();
        foreach (var integration in integrations)
        {
            var result = await webhookPublisher.PublishAsync(integration.WebhookUrl, integration.AuthHeader, payload);
            results.Add(result);
        }

        return results.ToArray();
    }

    private sealed record GovernanceSummary(
        int ComplianceScore,
        int OpenObligations,
        int CriticalDeficiencies,
        int ControlCoveragePercent,
        int EvidenceIntegrityScore,
        int EvidenceFreshnessScore,
        int FindingEvidencePercent,
        int LinkedEvidenceCount);

    private sealed record RiskHeatRow(string Severity, int FindingCount, int ActionCount);
}
