using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Normyx.Api.Utilities;
using Normyx.Application.Abstractions;
using Normyx.Infrastructure.Persistence;

namespace Normyx.Api.Endpoints;

public static class FindingEndpoints
{
    public static IEndpointRouteBuilder MapFindingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/findings").WithTags("Findings").RequireAuthorization().WithRequestValidation();

        group.MapGet("/assessment/{assessmentId:guid}", ListFindingsForAssessmentAsync);
        group.MapGet("/{findingId:guid}", GetFindingAsync);

        return app;
    }

    private static async Task<IResult> ListFindingsForAssessmentAsync([FromRoute] Guid assessmentId, NormyxDbContext dbContext, ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);

        var findings = await dbContext.Findings
            .Where(x => x.AssessmentId == assessmentId && x.Assessment.AiSystemVersion.AiSystem.TenantId == tenantId)
            .OrderByDescending(x => x.Severity)
            .Select(x => new
            {
                x.Id,
                x.Type,
                Severity = x.Severity.ToString(),
                x.Title,
                x.Description,
                x.AffectedComponentIds,
                x.EvidenceLinks
            })
            .ToListAsync();

        return Results.Ok(findings);
    }

    private static async Task<IResult> GetFindingAsync([FromRoute] Guid findingId, NormyxDbContext dbContext, ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);

        var finding = await dbContext.Findings
            .Where(x => x.Id == findingId && x.Assessment.AiSystemVersion.AiSystem.TenantId == tenantId)
            .Select(x => new
            {
                x.Id,
                x.AssessmentId,
                x.Type,
                Severity = x.Severity.ToString(),
                x.Title,
                x.Description,
                x.AffectedComponentIds,
                x.EvidenceLinks
            })
            .FirstOrDefaultAsync();

        return finding is null ? Results.NotFound() : Results.Ok(finding);
    }
}
