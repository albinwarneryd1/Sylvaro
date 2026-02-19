using System.Text;
using System.Text.Json;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Normyx.Api.Utilities;
using Normyx.Application.Abstractions;
using Normyx.Application.Security;
using Normyx.Domain.Entities;
using Normyx.Infrastructure.Persistence;

namespace Normyx.Api.Endpoints;

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
        NormyxDbContext dbContext,
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

        var linkedEvidence = await dbContext.EvidenceLinks
            .Where(x => x.EvidenceExcerpt.Document.TenantId == tenantId)
            .CountAsync();

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
            evidenceSummary = new { linkedEvidence },
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
            var lines = BuildPdfLines(request.ExportType, version, latestAssessment, actions, controls, linkedEvidence);
            outputBytes = await exportService.GeneratePdfAsync($"Normyx AI {request.ExportType}", lines);
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
                eventType = "normyx.export.generated",
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

    private static async Task<IResult> ListExportsAsync([FromRoute] Guid versionId, NormyxDbContext dbContext, ICurrentUserContext currentUser)
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

    private static async Task<IResult> DownloadExportAsync([FromRoute] Guid artifactId, NormyxDbContext dbContext, ICurrentUserContext currentUser, IObjectStorage objectStorage)
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

    private static List<string> BuildPdfLines(string exportType, AiSystemVersion version, Assessment? latestAssessment, List<ActionItem> actions, List<ControlInstance> controls, int linkedEvidence)
    {
        var lines = new List<string>
        {
            $"Export Type: {exportType}",
            $"AI System: {version.AiSystem.Name}",
            $"Version: {version.VersionNumber}",
            $"Generated: {DateTimeOffset.UtcNow:O}",
            $"Evidence links: {linkedEvidence}",
            ""
        };

        if (latestAssessment is not null)
        {
            lines.Add("Latest assessment scores:");
            lines.Add(latestAssessment.RiskScoresJson);
            lines.Add("Policy pack versions: " + string.Join(", ", latestAssessment.PolicyPackVersionRefs));
            lines.Add("");
        }

        lines.Add("Action plan:");
        lines.AddRange(actions.Select(x => $"- [{x.Priority}] {x.Title} ({x.Status}) owner={x.OwnerRole} signoff={x.ApprovedAt?.ToString("u") ?? "pending"}"));
        lines.Add("");
        lines.Add("Control instances:");
        lines.AddRange(controls.Select(x => $"- {x.ControlKey}: {x.Status} signoff={x.ApprovedAt?.ToString("u") ?? "pending"}"));

        return lines;
    }

    private static async Task<WebhookPublishResult[]> SendToEnabledWebhooksAsync(
        NormyxDbContext dbContext,
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
}
