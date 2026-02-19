namespace Normyx.Application.Abstractions;

public interface IDocumentTextExtractor
{
    Task<string?> ExtractTextAsync(string fileName, string contentType, byte[] content, CancellationToken cancellationToken = default);
}
