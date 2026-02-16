using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using FortiAnswer.Orchestrator.Services;

namespace FortiAnswer.Orchestrator.Functions;

public sealed class BlobIngestTriggerFunction
{
    private readonly IngestionOrchestrator _ingestor;
    private readonly ILogger<BlobIngestTriggerFunction> _logger;

    public BlobIngestTriggerFunction(IngestionOrchestrator ingestor, ILogger<BlobIngestTriggerFunction> logger)
    {
        _ingestor = ingestor;
        _logger = logger;
    }

    [Function("blob_ingest_trigger")]
    public async Task Run(
        [BlobTrigger("%BLOB_CONTAINER%/{name}", Connection = "BLOB_CONNECTION")] byte[] content,
        string name,
        CancellationToken ct)
    {
        _logger.LogInformation("Blob trigger fired: {name}, bytes={len}", name, content.Length);

        if (!name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) &&
            !name.EndsWith(".docx", StringComparison.OrdinalIgnoreCase) &&
            !name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Skip unsupported file: {name}", name);
            return;
        }

        var (files, chunks) = await _ingestor.RunSingleAsync(name, content, ct);

        _logger.LogInformation("Ingest done: files={files}, chunks={chunks}, name={name}", files, chunks, name);
    }

}
