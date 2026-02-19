using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Normyx.Api.Utilities;
using Normyx.Application.Abstractions;
using Normyx.Domain.Entities;
using Normyx.Infrastructure.Persistence;

namespace Normyx.Api.Endpoints;

public static class DocumentEndpoints
{
    public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/documents").WithTags("Documents").RequireAuthorization();

        group.MapGet("", ListDocumentsAsync);
        group.MapGet("/{documentId:guid}/excerpts", ListExcerptsAsync);
        group.MapPost("/upload", UploadDocumentAsync).DisableAntiforgery();
        group.MapGet("/{documentId:guid}/download", DownloadDocumentAsync);
        group.MapPost("/{documentId:guid}/excerpts", CreateExcerptAsync);
        group.MapPost("/evidence-links", CreateEvidenceLinkAsync);

        return app;
    }

    private static async Task<IResult> ListDocumentsAsync(NormyxDbContext dbContext, ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);

        var docs = await dbContext.Documents
            .Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.UploadedAt)
            .Select(x => new
            {
                x.Id,
                x.FileName,
                x.MimeType,
                x.UploadedAt,
                x.UploadedByUserId,
                x.Tags,
                ExcerptCount = x.Excerpts.Count
            })
            .ToListAsync();

        return Results.Ok(docs);
    }

    private static async Task<IResult> ListExcerptsAsync([FromRoute] Guid documentId, NormyxDbContext dbContext, ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);
        var documentInTenant = await dbContext.Documents.AnyAsync(x => x.Id == documentId && x.TenantId == tenantId);
        if (!documentInTenant)
        {
            return Results.NotFound();
        }

        var excerpts = await dbContext.EvidenceExcerpts
            .Where(x => x.DocumentId == documentId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new
            {
                x.Id,
                x.DocumentId,
                x.Title,
                x.Text,
                x.PageRef,
                x.CreatedByUserId,
                x.CreatedAt
            })
            .ToListAsync();

        return Results.Ok(excerpts);
    }

    private static async Task<IResult> UploadDocumentAsync(
        HttpRequest request,
        NormyxDbContext dbContext,
        ICurrentUserContext currentUser,
        IObjectStorage objectStorage,
        IDocumentTextExtractor textExtractor,
        IRagService ragService,
        CancellationToken cancellationToken)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);
        var userId = TenantContext.RequireUserId(currentUser);

        var form = await request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("file");
        if (file is null || file.Length == 0)
        {
            return Results.BadRequest(new { message = "File is required" });
        }

        await using var inputStream = file.OpenReadStream();
        using var memory = new MemoryStream();
        await inputStream.CopyToAsync(memory, cancellationToken);
        var bytes = memory.ToArray();
        memory.Position = 0;

        var storageRef = await objectStorage.SaveAsync(file.FileName, file.ContentType, memory, cancellationToken);

        var tags = form.TryGetValue("tags", out var tagValues)
            ? tagValues.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];

        var doc = new Document
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FileName = file.FileName,
            MimeType = file.ContentType,
            StorageRef = storageRef,
            UploadedByUserId = userId,
            UploadedAt = DateTimeOffset.UtcNow,
            Tags = tags
        };

        dbContext.Documents.Add(doc);
        await dbContext.SaveChangesAsync(cancellationToken);

        var extractedText = await textExtractor.ExtractTextAsync(file.FileName, file.ContentType, bytes, cancellationToken);
        if (!string.IsNullOrWhiteSpace(extractedText))
        {
            var excerpt = new EvidenceExcerpt
            {
                Id = Guid.NewGuid(),
                DocumentId = doc.Id,
                Title = "Auto Extracted Excerpt",
                Text = extractedText[..Math.Min(1200, extractedText.Length)],
                PageRef = "auto",
                CreatedByUserId = userId,
                CreatedAt = DateTimeOffset.UtcNow
            };

            dbContext.EvidenceExcerpts.Add(excerpt);
            await dbContext.SaveChangesAsync(cancellationToken);

            await ragService.IndexTextAsync(
                tenantId,
                doc.Id,
                "Document",
                extractedText,
                doc.Tags,
                cancellationToken);
        }

        return Results.Created($"/documents/{doc.Id}", new { doc.Id, doc.FileName, doc.UploadedAt, Extracted = !string.IsNullOrWhiteSpace(extractedText) });
    }

    private static async Task<IResult> DownloadDocumentAsync(
        [FromRoute] Guid documentId,
        NormyxDbContext dbContext,
        ICurrentUserContext currentUser,
        IObjectStorage objectStorage,
        CancellationToken cancellationToken)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);

        var doc = await dbContext.Documents
            .FirstOrDefaultAsync(x => x.Id == documentId && x.TenantId == tenantId, cancellationToken);

        if (doc is null)
        {
            return Results.NotFound();
        }

        var (stream, contentType) = await objectStorage.OpenReadAsync(doc.StorageRef, cancellationToken);
        return Results.File(stream, contentType == "application/octet-stream" ? doc.MimeType : contentType, doc.FileName);
    }

    private record CreateExcerptRequest(string Title, string Text, string PageRef);

    private static async Task<IResult> CreateExcerptAsync(
        [FromRoute] Guid documentId,
        [FromBody] CreateExcerptRequest request,
        NormyxDbContext dbContext,
        ICurrentUserContext currentUser,
        IRagService ragService)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);
        var userId = TenantContext.RequireUserId(currentUser);

        var doc = await dbContext.Documents.FirstOrDefaultAsync(x => x.Id == documentId && x.TenantId == tenantId);
        if (doc is null)
        {
            return Results.NotFound();
        }

        var excerpt = new EvidenceExcerpt
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            Title = request.Title,
            Text = request.Text,
            PageRef = request.PageRef,
            CreatedByUserId = userId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        dbContext.EvidenceExcerpts.Add(excerpt);
        await dbContext.SaveChangesAsync();

        await ragService.IndexTextAsync(
            tenantId,
            documentId,
            "EvidenceExcerpt",
            request.Text,
            ["manual-excerpt"],
            CancellationToken.None);

        return Results.Created($"/documents/{documentId}/excerpts/{excerpt.Id}", excerpt);
    }

    private record CreateEvidenceLinkRequest(string TargetType, Guid TargetId, Guid EvidenceExcerptId);

    private static async Task<IResult> CreateEvidenceLinkAsync(
        [FromBody] CreateEvidenceLinkRequest request,
        NormyxDbContext dbContext,
        ICurrentUserContext currentUser)
    {
        var tenantId = TenantContext.RequireTenantId(currentUser);

        var excerptInTenant = await dbContext.EvidenceExcerpts
            .AnyAsync(x => x.Id == request.EvidenceExcerptId && x.Document.TenantId == tenantId);

        if (!excerptInTenant)
        {
            return Results.NotFound();
        }

        var link = new EvidenceLink
        {
            Id = Guid.NewGuid(),
            TargetType = request.TargetType,
            TargetId = request.TargetId,
            EvidenceExcerptId = request.EvidenceExcerptId
        };

        dbContext.EvidenceLinks.Add(link);
        await dbContext.SaveChangesAsync();

        return Results.Created($"/documents/evidence-links/{link.Id}", link);
    }
}
