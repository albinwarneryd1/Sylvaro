using Microsoft.Extensions.Options;
using Normyx.Application.Abstractions;

namespace Normyx.Infrastructure.Storage;

public class LocalObjectStorage(IOptions<StorageOptions> options) : IObjectStorage
{
    private readonly string _rootPath = Path.GetFullPath(options.Value.RootPath);

    public async Task<string> SaveAsync(string fileName, string contentType, Stream stream, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_rootPath);

        var safeName = Path.GetFileName(fileName);
        var storageRef = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}-{safeName}";
        var fullPath = Path.Combine(_rootPath, storageRef);

        await using var output = File.Create(fullPath);
        await stream.CopyToAsync(output, cancellationToken);

        return storageRef;
    }

    public Task<(Stream Stream, string ContentType)> OpenReadAsync(string storageRef, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.Combine(_rootPath, storageRef);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Stored object not found", storageRef);
        }

        Stream stream = File.OpenRead(fullPath);
        return Task.FromResult((stream, "application/octet-stream"));
    }
}
