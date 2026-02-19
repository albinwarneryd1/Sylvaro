using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Normyx.Api.Utilities;
using Normyx.Application.Abstractions;
using Normyx.Infrastructure.Persistence;

namespace Normyx.Api.Endpoints;

public static class EvidenceEndpoints
{
    public static IEndpointRouteBuilder MapEvidenceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/versions/{versionId:guid}/evidence").WithTags("Evidence").RequireAuthorization().WithRequestValidation();

        group.MapGet("/map", GetEvidenceMapAsync);
        group.MapGet("/gaps", GetEvidenceGapsAsync);
        group.MapGet("/search", SearchEvidenceAsync);

        return app;
    }

    private static async Task<IResult> GetEvidenceMapAsync([FromRoute] Guid versionId, NormyxDbContext dbContext, ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);
        var versionExists = await dbContext.AiSystemVersions.AnyAsync(x => x.Id == versionId && x.AiSystem.TenantId == tenantId);
        if (!versionExists)
        {
            return Results.NotFound();
        }

        var actions = await dbContext.ActionItems.Where(x => x.AiSystemVersionId == versionId).ToListAsync();
        var controls = await dbContext.ControlInstances.Where(x => x.AiSystemVersionId == versionId).ToListAsync();

        var latestAssessmentId = await dbContext.Assessments
            .Where(x => x.AiSystemVersionId == versionId)
            .OrderByDescending(x => x.RanAt)
            .Select(x => x.Id)
            .FirstOrDefaultAsync();

        var findings = latestAssessmentId == Guid.Empty
            ? []
            : await dbContext.Findings.Where(x => x.AssessmentId == latestAssessmentId).ToListAsync();

        var allLinks = await dbContext.EvidenceLinks
            .Include(x => x.EvidenceExcerpt)
            .Where(x => x.EvidenceExcerpt.Document.TenantId == tenantId)
            .ToListAsync();

        var actionMap = actions.Select(action => new
        {
            targetType = "ActionItem",
            targetId = action.Id,
            title = action.Title,
            evidence = allLinks.Where(link => link.TargetType == "ActionItem" && link.TargetId == action.Id)
                .Select(link => new { link.Id, link.EvidenceExcerptId, link.EvidenceExcerpt.Title, link.EvidenceExcerpt.PageRef })
        });

        var findingMap = findings.Select(finding => new
        {
            targetType = "Finding",
            targetId = finding.Id,
            title = finding.Title,
            evidence = allLinks.Where(link => link.TargetType == "Finding" && link.TargetId == finding.Id)
                .Select(link => new { link.Id, link.EvidenceExcerptId, link.EvidenceExcerpt.Title, link.EvidenceExcerpt.PageRef })
        });

        var controlMap = controls.Select(control => new
        {
            targetType = "ControlInstance",
            targetId = control.Id,
            title = control.ControlKey,
            evidence = allLinks.Where(link => link.TargetType == "ControlInstance" && link.TargetId == control.Id)
                .Select(link => new { link.Id, link.EvidenceExcerptId, link.EvidenceExcerpt.Title, link.EvidenceExcerpt.PageRef })
        });

        return Results.Ok(new
        {
            actions = actionMap,
            findings = findingMap,
            controls = controlMap
        });
    }

    private static async Task<IResult> GetEvidenceGapsAsync([FromRoute] Guid versionId, NormyxDbContext dbContext, ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);

        var actionGaps = await dbContext.ActionItems
            .Where(x => x.AiSystemVersionId == versionId && x.AiSystemVersion.AiSystem.TenantId == tenantId)
            .Where(x => !dbContext.EvidenceLinks.Any(link => link.TargetType == "ActionItem" && link.TargetId == x.Id && link.EvidenceExcerpt.Document.TenantId == tenantId))
            .Select(x => new
            {
                targetType = "ActionItem",
                targetId = x.Id,
                title = x.Title,
                suggestedEvidence = x.AcceptanceCriteria
            })
            .ToListAsync();

        var controlGaps = await dbContext.ControlInstances
            .Where(x => x.AiSystemVersionId == versionId && x.AiSystemVersion.AiSystem.TenantId == tenantId)
            .Where(x => !dbContext.EvidenceLinks.Any(link => link.TargetType == "ControlInstance" && link.TargetId == x.Id && link.EvidenceExcerpt.Document.TenantId == tenantId))
            .Select(x => new
            {
                targetType = "ControlInstance",
                targetId = x.Id,
                title = x.ControlKey,
                suggestedEvidence = dbContext.Controls.Where(c => c.ControlKey == x.ControlKey).Select(c => string.Join(", ", c.EvidenceRequired)).FirstOrDefault() ?? "Control evidence"
            })
            .ToListAsync();

        return Results.Ok(new
        {
            totalGaps = actionGaps.Count + controlGaps.Count,
            actionGaps,
            controlGaps
        });
    }

    private static async Task<IResult> SearchEvidenceAsync([FromRoute] Guid versionId, [FromQuery] string query, [FromQuery] int topK, IRagService ragService, NormyxDbContext dbContext, ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);
        var versionExists = await dbContext.AiSystemVersions.AnyAsync(x => x.Id == versionId && x.AiSystem.TenantId == tenantId);
        if (!versionExists)
        {
            return Results.NotFound();
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return Results.BadRequest(new { message = "query is required" });
        }

        var results = await ragService.SearchAsync(tenantId, query, topK <= 0 ? 5 : topK);
        return Results.Ok(results);
    }
}
