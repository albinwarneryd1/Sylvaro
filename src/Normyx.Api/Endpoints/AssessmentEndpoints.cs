using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Normyx.Api.Utilities;
using Normyx.Application.Abstractions;
using Normyx.Infrastructure.Persistence;

namespace Normyx.Api.Endpoints;

public static class AssessmentEndpoints
{
    public static IEndpointRouteBuilder MapAssessmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/versions/{versionId:guid}/assessments").WithTags("Assessments").RequireAuthorization();

        group.MapPost("/run", RunAssessmentAsync);
        group.MapGet("", ListAssessmentsAsync);
        group.MapGet("/{assessmentId:guid}", GetAssessmentAsync);
        group.MapGet("/diff/{otherVersionId:guid}", CompareVersionAssessmentsAsync);

        return app;
    }

    private static async Task<IResult> RunAssessmentAsync([FromRoute] Guid versionId, IAssessmentService assessmentService, ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);
        var userId = TenantContext.RequireUserId(currentUser);

        try
        {
            var result = await assessmentService.RunAssessmentAsync(tenantId, versionId, userId);
            return Results.Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }

    private static async Task<IResult> ListAssessmentsAsync([FromRoute] Guid versionId, NormyxDbContext dbContext, ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);

        var assessments = await dbContext.Assessments
            .Where(x => x.AiSystemVersionId == versionId && x.AiSystemVersion.AiSystem.TenantId == tenantId)
            .OrderByDescending(x => x.RanAt)
            .Select(x => new { x.Id, x.RanAt, x.RanByUserId, x.LlmProvider, x.RiskScoresJson })
            .ToListAsync();

        return Results.Ok(assessments);
    }

    private static async Task<IResult> GetAssessmentAsync([FromRoute] Guid versionId, [FromRoute] Guid assessmentId, NormyxDbContext dbContext, ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);

        var assessment = await dbContext.Assessments
            .Where(x => x.Id == assessmentId && x.AiSystemVersionId == versionId && x.AiSystemVersion.AiSystem.TenantId == tenantId)
            .FirstOrDefaultAsync();

        if (assessment is null)
        {
            return Results.NotFound();
        }

        var response = new
        {
            assessment.Id,
            assessment.RanAt,
            assessment.RanByUserId,
            assessment.LlmProvider,
            summary = JsonDocument.Parse(assessment.SummaryJson).RootElement,
            riskScores = JsonDocument.Parse(assessment.RiskScoresJson).RootElement
        };

        return Results.Ok(response);
    }

    private static async Task<IResult> CompareVersionAssessmentsAsync([FromRoute] Guid versionId, [FromRoute] Guid otherVersionId, NormyxDbContext dbContext, ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);

        var first = await dbContext.Assessments
            .Where(x => x.AiSystemVersionId == versionId && x.AiSystemVersion.AiSystem.TenantId == tenantId)
            .OrderByDescending(x => x.RanAt)
            .FirstOrDefaultAsync();

        var second = await dbContext.Assessments
            .Where(x => x.AiSystemVersionId == otherVersionId && x.AiSystemVersion.AiSystem.TenantId == tenantId)
            .OrderByDescending(x => x.RanAt)
            .FirstOrDefaultAsync();

        if (first is null || second is null)
        {
            return Results.NotFound(new { message = "Assessments missing for one or both versions" });
        }

        var firstScores = JsonDocument.Parse(first.RiskScoresJson).RootElement;
        var secondScores = JsonDocument.Parse(second.RiskScoresJson).RootElement;

        var diff = new
        {
            fromVersionId = versionId,
            toVersionId = otherVersionId,
            scoreDelta = secondScores.GetProperty("totalScore").GetInt32() - firstScores.GetProperty("totalScore").GetInt32(),
            aiActClassChanged = firstScores.GetProperty("aiActClass").GetString() != secondScores.GetProperty("aiActClass").GetString(),
            from = firstScores,
            to = secondScores
        };

        return Results.Ok(diff);
    }
}
