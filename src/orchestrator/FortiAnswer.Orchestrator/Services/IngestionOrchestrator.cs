using System.Security.Cryptography;
using System.Text;

namespace FortiAnswer.Orchestrator.Services;

public sealed class IngestionOrchestrator
{
    private readonly BlobDocumentSource _source;
    private readonly TextChunker _chunker;
    private readonly IEmbeddingService _embeddings;
    private readonly AzureAiSearchIngestService _search;

    public IngestionOrchestrator(
        BlobDocumentSource source,
        TextChunker chunker,
        IEmbeddingService embeddings,
        AzureAiSearchIngestService search)
    {
        _source = source;
        _chunker = chunker;
        _embeddings = embeddings;
        _search = search;
    }

    public async Task<(int files, int chunks)> RunAsync(string? prefix, int maxFiles, CancellationToken ct)
    {
        int files = 0;
        int chunksTotal = 0;

        // ✅ IMPORTANT: ListAndReadAsync(prefix, maxFiles, ct) returns a LIST (not async enumerable)
        var docs = await _source.ListAndReadAsync(prefix, maxFiles, ct);

        foreach (var sd in docs)
        {
            files++;

            var now = DateTimeOffset.UtcNow;
            int chunkid = 0;

            // Chunk doc text
            var chunks = _chunker.ChunkByChars(sd.Content, 1200, 150).ToList();

            // Embed + upload in small batches (safe for RPM)
            const int batchSize = 16;

            for (int i = 0; i < chunks.Count; i += batchSize)
            {
                var batch = chunks.Skip(i).Take(batchSize).ToList();

                // Simple: call EmbedAsync per chunk (works now)
                var vectors = new List<float[]>(batch.Count);
                foreach (var text in batch)
                {
                    vectors.Add(await _embeddings.EmbedAsync(text, ct));
                }

                var actions = new List<object>(batch.Count);

                for (int j = 0; j < batch.Count; j++)
                {
                    chunkid++;
                    chunksTotal++;

                    var id = MakeSafeId(sd.Path, chunkid);

                    actions.Add(new Dictionary<string, object>
                    {
                        ["@search.action"] = "upload",
                        ["id"] = id,
                        ["content"] = batch[j],
                        ["source"] = sd.SourceId,        // ✅ stable safe id
                        ["path"] = sd.Path,
                        ["chunkid"] = chunkid,           // ✅ must match index field name
                        ["page"] = 1,
                        ["createdUtc"] = now,
                        ["contentVector"] = vectors[j]   // ✅ must match index vector field
                    });
                }

                await _search.UploadAsync(actions, ct);
            }
        }

        return (files, chunksTotal);
    }

    private static string MakeSafeId(string path, int chunkid)
    {
        var raw = $"{path}::chunk::{chunkid}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        var base64 = Convert.ToBase64String(hash)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return $"b_{base64}";
    }
}
