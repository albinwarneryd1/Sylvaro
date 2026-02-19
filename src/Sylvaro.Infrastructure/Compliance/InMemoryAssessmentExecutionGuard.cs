using System.Collections.Concurrent;
using Normyx.Application.Abstractions;

namespace Normyx.Infrastructure.Compliance;

public sealed class InMemoryAssessmentExecutionGuard : IAssessmentExecutionGuard
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public async ValueTask<IAsyncDisposable> AcquireAsync(Guid tenantId, Guid versionId, CancellationToken cancellationToken = default)
    {
        var key = $"{tenantId:N}:{versionId:N}";
        var semaphore = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        return new Releaser(semaphore);
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            semaphore.Release();
            return ValueTask.CompletedTask;
        }
    }
}
