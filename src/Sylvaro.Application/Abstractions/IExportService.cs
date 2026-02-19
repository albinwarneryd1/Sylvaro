namespace Normyx.Application.Abstractions;

public interface IExportService
{
    Task<byte[]> GeneratePdfAsync(string title, IReadOnlyCollection<string> lines, CancellationToken cancellationToken = default);
}
