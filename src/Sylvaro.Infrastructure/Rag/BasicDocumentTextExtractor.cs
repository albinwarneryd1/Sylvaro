using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Normyx.Application.Abstractions;

namespace Normyx.Infrastructure.Rag;

public class BasicDocumentTextExtractor : IDocumentTextExtractor
{
    public Task<string?> ExtractTextAsync(string fileName, string contentType, byte[] content, CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        if (contentType.Contains("text/plain", StringComparison.OrdinalIgnoreCase) || extension == ".txt")
        {
            return Task.FromResult<string?>(Encoding.UTF8.GetString(content));
        }

        if (contentType.Contains("wordprocessingml", StringComparison.OrdinalIgnoreCase) || extension == ".docx")
        {
            return Task.FromResult(ExtractDocxText(content));
        }

        return Task.FromResult<string?>(null);
    }

    private static string? ExtractDocxText(byte[] content)
    {
        using var stream = new MemoryStream(content);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var entry = archive.GetEntry("word/document.xml");
        if (entry is null)
        {
            return null;
        }

        using var xmlStream = entry.Open();
        using var reader = new StreamReader(xmlStream, Encoding.UTF8);
        var xml = reader.ReadToEnd();

        var text = Regex.Replace(xml, "<[^>]+>", " ");
        text = Regex.Replace(text, "\\s+", " ").Trim();

        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}
