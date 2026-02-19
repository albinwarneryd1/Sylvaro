using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Normyx.Api.Utilities;
using Normyx.Application.Abstractions;
using Normyx.Application.Security;
using Normyx.Domain.Entities;
using Normyx.Domain.Enums;
using Normyx.Infrastructure.Persistence;

namespace Normyx.Api.Endpoints;

public static class ActionEndpoints
{
    public static IEndpointRouteBuilder MapActionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/actions").WithTags("Actions").RequireAuthorization();

        group.MapGet("/version/{versionId:guid}", ListActionsAsync);
        group.MapGet("/board/{versionId:guid}", ActionBoardAsync);
        group.MapPut("/{actionId:guid}/status", UpdateStatusAsync);
        group.MapPost("/{actionId:guid}/approve", ApproveActionAsync)
            .RequireAuthorization(new AuthorizeAttribute { Roles = $"{RoleNames.Admin},{RoleNames.ComplianceOfficer}" });
        group.MapGet("/{actionId:guid}/reviews", ListReviewsAsync);
        group.MapPost("/{actionId:guid}/reviews", ReviewActionAsync)
            .RequireAuthorization(new AuthorizeAttribute { Roles = $"{RoleNames.Admin},{RoleNames.ComplianceOfficer}" });

        return app;
    }

    private static async Task<IResult> ListActionsAsync([FromRoute] Guid versionId, NormyxDbContext dbContext, ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);

        var actions = await dbContext.ActionItems
            .Where(x => x.AiSystemVersionId == versionId && x.AiSystemVersion.AiSystem.TenantId == tenantId)
            .OrderBy(x => x.Priority)
            .ThenBy(x => x.Status)
            .ToListAsync();

        return Results.Ok(actions.Select(x => new
        {
            x.Id,
            x.Title,
            x.Description,
            x.Priority,
            x.OwnerRole,
            Status = x.Status.ToString(),
            x.AcceptanceCriteria,
            x.DueDate,
            x.SourceFindingId,
            x.ApprovedBy,
            x.ApprovedAt
        }));
    }

    private static async Task<IResult> ActionBoardAsync([FromRoute] Guid versionId, NormyxDbContext dbContext, ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);

        var actions = await dbContext.ActionItems
            .Where(x => x.AiSystemVersionId == versionId && x.AiSystemVersion.AiSystem.TenantId == tenantId)
            .ToListAsync();

        static object Shape(ActionItem x) => new
        {
            x.Id,
            x.Title,
            x.Description,
            x.Priority,
            x.OwnerRole,
            Status = x.Status.ToString(),
            x.AcceptanceCriteria,
            x.DueDate,
            x.SourceFindingId,
            x.ApprovedBy,
            x.ApprovedAt
        };

        var board = new
        {
            New = actions.Where(x => x.Status == ActionStatus.New).Select(Shape),
            InProgress = actions.Where(x => x.Status == ActionStatus.InProgress).Select(Shape),
            Done = actions.Where(x => x.Status == ActionStatus.Done).Select(Shape),
            AcceptedRisk = actions.Where(x => x.Status == ActionStatus.AcceptedRisk).Select(Shape)
        };

        return Results.Ok(board);
    }

    public record UpdateActionStatusRequest(ActionStatus Status);

    private static async Task<IResult> UpdateStatusAsync([FromRoute] Guid actionId, [FromBody] UpdateActionStatusRequest request, NormyxDbContext dbContext, ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);

        var action = await dbContext.ActionItems
            .FirstOrDefaultAsync(x => x.Id == actionId && x.AiSystemVersion.AiSystem.TenantId == tenantId);

        if (action is null)
        {
            return Results.NotFound();
        }

        action.Status = request.Status;
        await dbContext.SaveChangesAsync();
        return Results.NoContent();
    }

    public record ApproveActionRequest(string Comment);

    private static async Task<IResult> ApproveActionAsync([FromRoute] Guid actionId, [FromBody] ApproveActionRequest request, NormyxDbContext dbContext, ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);
        var userId = TenantContext.RequireUserId(currentUser);

        var action = await dbContext.ActionItems
            .FirstOrDefaultAsync(x => x.Id == actionId && x.AiSystemVersion.AiSystem.TenantId == tenantId);

        if (action is null)
        {
            return Results.NotFound();
        }

        action.ApprovedBy = userId;
        action.ApprovedAt = DateTimeOffset.UtcNow;
        action.Status = ActionStatus.Done;
        action.Description = string.IsNullOrWhiteSpace(request.Comment)
            ? action.Description
            : action.Description + "\nApproval comment: " + request.Comment;

        await dbContext.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> ListReviewsAsync([FromRoute] Guid actionId, NormyxDbContext dbContext, ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);

        var reviews = await dbContext.ActionReviews
            .Where(x => x.ActionItemId == actionId && dbContext.ActionItems
                .Any(action => action.Id == x.ActionItemId && action.AiSystemVersion.AiSystem.TenantId == tenantId))
            .OrderByDescending(x => x.ReviewedAt)
            .Select(x => new
            {
                x.Id,
                x.ActionItemId,
                x.ReviewedByUserId,
                Decision = x.Decision.ToString(),
                x.Comment,
                x.ReviewedAt
            })
            .ToListAsync();

        return Results.Ok(reviews);
    }

    public record ReviewActionRequest(ReviewDecision Decision, string Comment);

    private static async Task<IResult> ReviewActionAsync(
        [FromRoute] Guid actionId,
        [FromBody] ReviewActionRequest request,
        NormyxDbContext dbContext,
        ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);
        var userId = TenantContext.RequireUserId(currentUser);

        var action = await dbContext.ActionItems
            .FirstOrDefaultAsync(x => x.Id == actionId && x.AiSystemVersion.AiSystem.TenantId == tenantId);

        if (action is null)
        {
            return Results.NotFound();
        }

        var review = new ActionReview
        {
            Id = Guid.NewGuid(),
            ActionItemId = actionId,
            ReviewedByUserId = userId,
            Decision = request.Decision,
            Comment = request.Comment,
            ReviewedAt = DateTimeOffset.UtcNow
        };

        switch (request.Decision)
        {
            case ReviewDecision.Approved:
                action.Status = ActionStatus.Done;
                action.ApprovedBy = userId;
                action.ApprovedAt = DateTimeOffset.UtcNow;
                break;
            case ReviewDecision.Rejected:
                action.Status = ActionStatus.AcceptedRisk;
                action.ApprovedBy = null;
                action.ApprovedAt = null;
                break;
            case ReviewDecision.NeedsEdits:
                action.Status = ActionStatus.InProgress;
                action.ApprovedBy = null;
                action.ApprovedAt = null;
                break;
        }

        if (!string.IsNullOrWhiteSpace(request.Comment))
        {
            action.Description += $"\nReview ({request.Decision}): {request.Comment}";
        }

        dbContext.ActionReviews.Add(review);
        await dbContext.SaveChangesAsync();
        return Results.Created($"/actions/{actionId}/reviews/{review.Id}", new { review.Id, Decision = review.Decision.ToString(), review.ReviewedAt });
    }
}
