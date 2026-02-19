using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Normyx.Api.Utilities;
using Normyx.Application.Abstractions;
using Normyx.Domain.Enums;
using Normyx.Infrastructure.Persistence;

namespace Normyx.Api.Endpoints;

public static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/dashboard").WithTags("Dashboard").RequireAuthorization();

        group.MapGet("/tenant", TenantDashboardAsync);
        group.MapGet("/system/{systemId:guid}", SystemDashboardAsync);

        return app;
    }

    private static async Task<IResult> TenantDashboardAsync(NormyxDbContext dbContext, ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);

        var systemCount = await dbContext.AiSystems.CountAsync(x => x.TenantId == tenantId);
        var openActions = await dbContext.ActionItems.CountAsync(x => x.AiSystemVersion.AiSystem.TenantId == tenantId && x.Status != ActionStatus.Done && x.Status != ActionStatus.AcceptedRisk);
        var latestAssessments = await dbContext.Assessments
            .Where(x => x.AiSystemVersion.AiSystem.TenantId == tenantId)
            .OrderByDescending(x => x.RanAt)
            .Take(50)
            .ToListAsync();

        var riskDistribution = latestAssessments
            .Select(x => x.RiskScoresJson)
            .Select(raw => raw.Contains("high-risk", StringComparison.OrdinalIgnoreCase) ? "high-risk" : raw.Contains("limited", StringComparison.OrdinalIgnoreCase) ? "limited" : "minimal")
            .GroupBy(x => x)
            .ToDictionary(g => g.Key, g => g.Count());

        return Results.Ok(new { systemCount, openActions, riskDistribution });
    }

    private static async Task<IResult> SystemDashboardAsync([FromRoute] Guid systemId, NormyxDbContext dbContext, ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);

        var system = await dbContext.AiSystems.FirstOrDefaultAsync(x => x.Id == systemId && x.TenantId == tenantId);
        if (system is null)
        {
            return Results.NotFound();
        }

        var versionIds = await dbContext.AiSystemVersions.Where(x => x.AiSystemId == systemId).Select(x => x.Id).ToListAsync();

        var assessments = await dbContext.Assessments
            .Where(x => versionIds.Contains(x.AiSystemVersionId))
            .OrderBy(x => x.RanAt)
            .Select(x => new { x.Id, x.RanAt, x.RiskScoresJson })
            .ToListAsync();

        var scoreTrend = assessments.Select(x => new
        {
            x.Id,
            x.RanAt,
            score = ExtractTotalScore(x.RiskScoresJson)
        });

        return Results.Ok(new
        {
            system.Id,
            system.Name,
            system.Status,
            assessmentsCount = assessments.Count,
            scoreTrend
        });
    }

    private static int ExtractTotalScore(string riskScoresJson)
    {
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
}
