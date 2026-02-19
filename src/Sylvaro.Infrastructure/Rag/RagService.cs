using Microsoft.EntityFrameworkCore;
using Normyx.Application.Abstractions;
using Normyx.Application.Rag;
using Normyx.Domain.Entities;
using Normyx.Infrastructure.Persistence;

namespace Normyx.Infrastructure.Rag;

public class RagService(NormyxDbContext dbContext) : IRagService
{
    private const int VectorSize = 128;

    public async Task SeedReferenceNotesAsync(CancellationToken cancellationToken = default)
    {
        var globalTenantId = Guid.Empty;
        var hasAnyGlobalReferences = await dbContext.RagChunks
            .AnyAsync(x => x.TenantId == globalTenantId && x.SourceType == "ReferenceNote", cancellationToken);

        if (hasAnyGlobalReferences)
        {
            return;
        }

        var root = ResolveReferenceRoot();
        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(root, "*.md", SearchOption.TopDirectoryOnly))
        {
            var text = await File.ReadAllTextAsync(file, cancellationToken);
            await IndexTextInternalAsync(globalTenantId, null, "ReferenceNote", text, [Path.GetFileNameWithoutExtension(file)], replaceExistingForDocument: false, cancellationToken);
        }
    }

    public Task IndexTextAsync(Guid tenantId, Guid? documentId, string sourceType, string text, string[] tags, CancellationToken cancellationToken = default)
        => IndexTextInternalAsync(tenantId, documentId, sourceType, text, tags, replaceExistingForDocument: documentId.HasValue, cancellationToken);

    public async Task<IReadOnlyCollection<RagSearchResult>> SearchAsync(Guid tenantId, string query, int topK = 5, CancellationToken cancellationToken = default)
    {
        var queryEmbedding = Embed(query);

        var chunks = await dbContext.RagChunks
            .Where(x => x.TenantId == tenantId || x.TenantId == Guid.Empty)
            .ToListAsync(cancellationToken);

        var scored = chunks
            .Select(chunk => new RagSearchResult(
                chunk.Id,
                chunk.SourceType,
                chunk.DocumentId,
                chunk.ChunkText,
                CosineSimilarity(queryEmbedding, chunk.Embedding),
                chunk.Tags))
            .OrderByDescending(x => x.Score)
            .Take(Math.Max(1, Math.Min(topK, 20)))
            .ToArray();

        return scored;
    }

    private async Task IndexTextInternalAsync(Guid tenantId, Guid? documentId, string sourceType, string text, string[] tags, bool replaceExistingForDocument, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (replaceExistingForDocument && documentId.HasValue)
        {
            var existing = dbContext.RagChunks.Where(x => x.TenantId == tenantId && x.DocumentId == documentId);
            dbContext.RagChunks.RemoveRange(existing);
        }

        foreach (var chunk in ChunkText(text))
        {
            dbContext.RagChunks.Add(new RagChunk
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                DocumentId = documentId,
                SourceType = sourceType,
                ChunkText = chunk,
                Tags = tags,
                Embedding = Embed(chunk),
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string ResolveReferenceRoot()
    {
        var direct = Path.Combine(AppContext.BaseDirectory, "reference-notes");
        if (Directory.Exists(direct))
        {
            return direct;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "reference-notes"));
    }

    private static IEnumerable<string> ChunkText(string text)
    {
        const int chunkSize = 1000;
        const int overlap = 150;

        var normalized = text.Replace("\r", " ").Replace("\n", " ");
        if (normalized.Length <= chunkSize)
        {
            yield return normalized;
            yield break;
        }

        var index = 0;
        while (index < normalized.Length)
        {
            var length = Math.Min(chunkSize, normalized.Length - index);
            yield return normalized.Substring(index, length);
            if (index + length >= normalized.Length)
            {
                break;
            }

            index += chunkSize - overlap;
        }
    }

    private static float[] Embed(string text)
    {
        var vector = new float[VectorSize];

        foreach (var token in text
                     .ToLowerInvariant()
                     .Split([' ', '.', ',', ';', ':', '\n', '\t', '(', ')', '[', ']', '{', '}', '/', '\\', '"', '\'', '!', '?'], StringSplitOptions.RemoveEmptyEntries))
        {
            var hash = token.GetHashCode(StringComparison.Ordinal);
            var index = Math.Abs(hash % VectorSize);
            vector[index] += 1f;
        }

        var norm = (float)Math.Sqrt(vector.Sum(x => x * x));
        if (norm <= 0.00001f)
        {
            return vector;
        }

        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] /= norm;
        }

        return vector;
    }

    private static float CosineSimilarity(float[] left, float[] right)
    {
        if (left.Length == 0 || right.Length == 0)
        {
            return 0f;
        }

        var max = Math.Min(left.Length, right.Length);
        var dot = 0f;
        for (var i = 0; i < max; i++)
        {
            dot += left[i] * right[i];
        }

        return dot;
    }
}
