using System.Security.Cryptography;
using System.Text;

namespace FortiAnswer.Orchestrator.Services;

public sealed class IngestionOrchestrator
{
    private readonly BlobDocumentSource _source;
    private readonly TextChunker _chunker;
    private readonly IEmbeddingService _embeddings;
    private readonly AzureAiSearchIngestService _search;
    private readonly PdfTextExtractor _pdf;
    private readonly DocxTextExtractor _docx;

    public IngestionOrchestrator(
        BlobDocumentSource source,
        TextChunker chunker,
        IEmbeddingService embeddings,
        AzureAiSearchIngestService search,
        PdfTextExtractor pdf,
        DocxTextExtractor docx)
    {
        _source = source;
        _chunker = chunker;
        _embeddings = embeddings;
        _search = search;
        _pdf = pdf;
        _docx = docx;
    }

    // Batch mode
    public async Task<(int files, int chunks)> RunAsync(string? prefix, int maxFiles, CancellationToken ct)
    {
        int files = 0;
        int chunksTotal = 0;

        var docs = await _source.ListAndReadAsync(prefix, maxFiles, ct);

        foreach (var sd in docs)
        {
            files++;
            chunksTotal += await IngestTextAsync(
                sourceId: sd.SourceId,
                path: sd.Path,
                content: sd.Content,
                ct: ct);
        }

        return (files, chunksTotal);
    }

    // ✅ Single-file mode for BlobTrigger
    public async Task<(int files, int chunks)> RunSingleAsync(string blobName, byte[] contentBytes, CancellationToken ct)
    {
        string text;

        if (blobName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            text = Encoding.UTF8.GetString(contentBytes);
        }
        else if (blobName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            using var ms = new MemoryStream(contentBytes);
            text = await _pdf.ExtractAsync(ms, ct);
        }
        else if (blobName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
        {
            using var ms = new MemoryStream(contentBytes);
            text = await _docx.ExtractAsync(ms, ct);
        }
        else
        {
            return (0, 0); // unsupported
        }

        if (string.IsNullOrWhiteSpace(text))
            return (0, 0);

        // Keep stable; if you prefer hash, we can swap later
        var sourceId = blobName;

        var chunks = await IngestTextAsync(sourceId, blobName, text, ct);
        return (1, chunks);
    }

    // Shared ingestion pipeline: chunk -> embed -> upload
    private async Task<int> IngestTextAsync(string sourceId, string path, string content, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        int chunkid = 0;
        int chunksTotal = 0;

        // ✅ NEW: infer classification from path prefix (public/internal/confidential/restricted)
        // Example path: "public/FAQ-PUBLIC-VPN-TROUBLESHOOTING-20260217.docx"
        var classification = InferClassification(path);
        var docType = InferDocTypeFromName(path); // optional, useful later

        var chunks = _chunker.ChunkByChars(content, 1200, 150).ToList();
        const int batchSize = 16;

        for (int i = 0; i < chunks.Count; i += batchSize)
        {
            var batch = chunks.Skip(i).Take(batchSize).ToList();

            var vectors = new List<float[]>(batch.Count);
            foreach (var t in batch)
                vectors.Add(await _embeddings.EmbedAsync(t, ct));

            var actions = new List<object>(batch.Count);

            for (int j = 0; j < batch.Count; j++)
            {
                chunkid++;
                chunksTotal++;

                var id = MakeSafeId(path, chunkid);

                actions.Add(new Dictionary<string, object>
                {
                    ["@search.action"] = "upload",
                    ["id"] = id,
                    ["content"] = batch[j],
                    ["source"] = sourceId,
                    ["path"] = path,

                    // ✅ NEW: security classification for RBAC filtering
                    ["classification"] = classification,    // "public" | "internal" | "confidential" | "restricted" | "unknown"
            

                    ["chunkid"] = chunkid,
                    ["page"] = 1,
                    ["createdUtc"] = now,
                    ["contentVector"] = vectors[j]
                });
            }

            await _search.UploadAsync(actions, ct);
        }

        return chunksTotal;
    }

    private static string MakeSafeId(string path, int chunkid)
    {
        var raw = $"{path}::chunk::{chunkid}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        var base64 = Convert.ToBase64String(hash)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return $"b_{base64}";
    }

    /// <summary>
    /// Infer classification from blob path prefix.
    /// Expected: public/..., internal/..., confidential/..., restricted/...
    /// If not matched, returns "unknown" (safer than defaulting to public).
    /// </summary>
    private static string InferClassification(string path)
    {
        var p = (path ?? "").Trim().Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(p)) return "unknown";

        // first segment before '/'
        var idx = p.IndexOf('/');
        var first = (idx >= 0 ? p[..idx] : p).Trim().ToLowerInvariant();

        return first switch
        {
            "public" => "public",
            "internal" => "internal",
            "confidential" => "confidential",
            "restricted" => "restricted",
            _ => "unknown"
        };
    }

    /// <summary>
    /// Optional: infer docType from filename.
    /// </summary>
    private static string InferDocTypeFromName(string path)
    {
        var p = (path ?? "").Replace('\\', '/');
        var name = p.Split('/').LastOrDefault() ?? p;
        name = name.ToLowerInvariant();

        if (name.StartsWith("faq-")) return "faq";
        if (name.StartsWith("policy-")) return "policy";
        return "doc";
    }
}