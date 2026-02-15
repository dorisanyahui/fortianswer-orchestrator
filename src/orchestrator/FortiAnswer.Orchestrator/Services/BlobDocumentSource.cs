using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Configuration;

namespace FortiAnswer.Orchestrator.Services;

public sealed record SourceDocument(
    string BlobName,
    string SourceId,
    string Title,
    string Path,
    string Content
);

public sealed class BlobDocumentSource
{
    private readonly BlobContainerClient _container;
    private readonly IDocumentTextExtractor _extractor;

    public BlobDocumentSource(IConfiguration cfg, IDocumentTextExtractor extractor)
    {
        _extractor = extractor;

        var conn = cfg["BLOB_CONNECTION"] ?? throw new InvalidOperationException("BLOB_CONNECTION missing");
        var containerName = cfg["BLOB_CONTAINER"] ?? throw new InvalidOperationException("BLOB_CONTAINER missing");

        _container = new BlobContainerClient(conn, containerName);
    }

    public async Task<IReadOnlyList<SourceDocument>> ListAndReadAsync(
        string? prefix,
        int maxFiles,
        CancellationToken ct = default)
    {
        prefix ??= "";
        if (maxFiles <= 0) maxFiles = 10;

        var results = new List<SourceDocument>(maxFiles);

        // Ensure container exists (optional; safe)
        await _container.CreateIfNotExistsAsync(cancellationToken: ct);

        // List blobs
        await foreach (var item in _container.GetBlobsAsync(
                           traits: BlobTraits.None,
                           states: BlobStates.None,
                           prefix: string.IsNullOrWhiteSpace(prefix) ? null : prefix,
                           cancellationToken: ct))
        {
            if (results.Count >= maxFiles) break;

            var blobName = item.Name;
            var ext = Path.GetExtension(blobName).ToLowerInvariant();

            if (ext is not (".pdf" or ".docx" or ".txt" or ".md"))
                continue;

            var blob = _container.GetBlobClient(blobName);

            // Download to stream
            var dl = await blob.DownloadStreamingAsync(cancellationToken: ct);
            await using var stream = dl.Value.Content;

            string text;
            try
            {
                text = await _extractor.ExtractTextAsync(blobName, stream, ct);
            }
            catch (Exception ex)
            {
                // Skip bad file but keep moving (you can change to throw if you prefer)
                text = $"[EXTRACT_ERROR] {ex.GetType().Name}: {ex.Message}";
            }

            // Basic metadata
            var title = Path.GetFileName(blobName);
            var sourceId = MakeSafeId(blobName);

            results.Add(new SourceDocument(
                BlobName: blobName,
                SourceId: sourceId,
                Title: title,
                Path: blobName,
                Content: text ?? ""
            ));
        }

        return results;
    }

    // Azure AI Search doc keys must be safe
    private static string MakeSafeId(string input)
    {
        // URL-safe base64
        var bytes = System.Text.Encoding.UTF8.GetBytes(input);
        var b64 = Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        // prefix to avoid empty
        return "b_" + b64;
    }
}
