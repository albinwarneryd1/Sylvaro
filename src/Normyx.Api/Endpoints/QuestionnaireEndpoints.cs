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

public static class QuestionnaireEndpoints
{
    public static IEndpointRouteBuilder MapQuestionnaireEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/versions/{versionId:guid}/questionnaire").WithTags("Questionnaire").RequireAuthorization().WithRequestValidation();
        var writeRoles = $"{RoleNames.Admin},{RoleNames.ComplianceOfficer},{RoleNames.SecurityLead},{RoleNames.ProductOwner}";

        group.MapGet("", GetQuestionnaireAsync);
        group.MapPut("", UpsertQuestionnaireAsync).RequireAuthorization(new AuthorizeAttribute { Roles = writeRoles });

        return app;
    }

    private static async Task<IResult> GetQuestionnaireAsync([FromRoute] Guid versionId, NormyxDbContext dbContext, ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);
        var questionnaire = await dbContext.ComplianceQuestionnaires
            .FirstOrDefaultAsync(x => x.AiSystemVersionId == versionId && x.AiSystemVersion.AiSystem.TenantId == tenantId);

        if (questionnaire is null)
        {
            return Results.Ok(new { versionId, answers = new Dictionary<string, string>() });
        }

        var answers = JsonSerializer.Deserialize<Dictionary<string, string>>(questionnaire.AnswersJson) ?? new Dictionary<string, string>();
        return Results.Ok(new { versionId, answers, questionnaire.UpdatedAt, questionnaire.UpdatedByUserId });
    }

    public record UpsertQuestionnaireRequest([property: Required, MinLength(1)] Dictionary<string, string> Answers);

    private static async Task<IResult> UpsertQuestionnaireAsync(
        [FromRoute] Guid versionId,
        [FromBody] UpsertQuestionnaireRequest request,
        NormyxDbContext dbContext,
        ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);
        var userId = TenantContext.RequireUserId(currentUser);

        var versionExists = await dbContext.AiSystemVersions.AnyAsync(x => x.Id == versionId && x.AiSystem.TenantId == tenantId);
        if (!versionExists)
        {
            return Results.NotFound();
        }

        var questionnaire = await dbContext.ComplianceQuestionnaires.FirstOrDefaultAsync(x => x.AiSystemVersionId == versionId);
        if (questionnaire is null)
        {
            questionnaire = new ComplianceQuestionnaire
            {
                Id = Guid.NewGuid(),
                AiSystemVersionId = versionId
            };
            dbContext.ComplianceQuestionnaires.Add(questionnaire);
        }

        questionnaire.AnswersJson = JsonSerializer.Serialize(request.Answers);
        questionnaire.UpdatedByUserId = userId;
        questionnaire.UpdatedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync();
        return Results.NoContent();
    }
}
