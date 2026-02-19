using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Normyx.Api.Utilities;
using Normyx.Application.Abstractions;
using Normyx.Application.Security;
using Normyx.Domain.Entities;
using Normyx.Infrastructure.Persistence;

namespace Normyx.Api.Endpoints;

public static class IntegrationEndpoints
{
    public static IEndpointRouteBuilder MapIntegrationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/integrations").WithTags("Integrations").RequireAuthorization(
            new AuthorizeAttribute { Roles = RoleNames.Admin });

        group.MapGet("/webhooks", ListWebhooksAsync);
        group.MapPut("/webhooks/{provider}", UpsertWebhookAsync);
        group.MapPost("/webhooks/{provider}/test", TestWebhookAsync);

        return app;
    }

    private static async Task<IResult> ListWebhooksAsync(NormyxDbContext dbContext, ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);

        var integrations = await dbContext.TenantIntegrations
            .Where(x => x.TenantId == tenantId)
            .OrderBy(x => x.Provider)
            .Select(x => new
            {
                x.Id,
                x.Provider,
                x.WebhookUrl,
                x.IsEnabled,
                x.CreatedAt,
                x.UpdatedAt
            })
            .ToListAsync();

        return Results.Ok(integrations);
    }

    private record UpsertWebhookRequest(string WebhookUrl, string? AuthHeader, bool IsEnabled);

    private static async Task<IResult> UpsertWebhookAsync(
        [FromRoute] string provider,
        [FromBody] UpsertWebhookRequest request,
        NormyxDbContext dbContext,
        ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);

        var existing = await dbContext.TenantIntegrations
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Provider == provider);

        if (existing is null)
        {
            existing = new TenantIntegration
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Provider = provider,
                CreatedAt = DateTimeOffset.UtcNow
            };

            dbContext.TenantIntegrations.Add(existing);
        }

        existing.WebhookUrl = request.WebhookUrl;
        existing.AuthHeader = request.AuthHeader;
        existing.IsEnabled = request.IsEnabled;
        existing.UpdatedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> TestWebhookAsync(
        [FromRoute] string provider,
        NormyxDbContext dbContext,
        ICurrentUserContext currentUser,
        IWebhookPublisher webhookPublisher)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);

        var integration = await dbContext.TenantIntegrations
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Provider == provider && x.IsEnabled);

        if (integration is null)
        {
            return Results.NotFound(new { message = "Enabled webhook integration not found." });
        }

        var payload = new
        {
            eventType = "normyx.webhook.test",
            timestamp = DateTimeOffset.UtcNow,
            tenantId,
            provider,
            message = "Normyx webhook test event"
        };

        var result = await webhookPublisher.PublishAsync(integration.WebhookUrl, integration.AuthHeader, payload);
        return Results.Ok(result);
    }
}
