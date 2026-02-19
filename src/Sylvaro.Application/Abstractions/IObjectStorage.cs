namespace Normyx.Application.Abstractions;

public interface IObjectStorage
{
    Task<string> SaveAsync(string fileName, string contentType, Stream stream, CancellationToken cancellationToken = default);
    Task<(Stream Stream, string ContentType)> OpenReadAsync(string storageRef, CancellationToken cancellationToken = default);
}
