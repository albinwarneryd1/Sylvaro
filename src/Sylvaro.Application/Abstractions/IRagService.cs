using Normyx.Application.Rag;

namespace Normyx.Application.Abstractions;

public interface IRagService
{
    Task SeedReferenceNotesAsync(CancellationToken cancellationToken = default);

    Task IndexTextAsync(
        Guid tenantId,
        Guid? documentId,
        string sourceType,
        string text,
        string[] tags,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<RagSearchResult>> SearchAsync(
        Guid tenantId,
        string query,
        int topK = 5,
        CancellationToken cancellationToken = default);
}
