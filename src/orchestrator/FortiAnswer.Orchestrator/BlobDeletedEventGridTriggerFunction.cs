using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Azure.Messaging.EventGrid; // from EventGrid extension package
using FortiAnswer.Orchestrator.Services;

namespace FortiAnswer.Orchestrator.Functions;

public sealed class BlobDeletedEventGridTriggerFunction
{
    private readonly ILogger _log;
    private readonly AzureAiSearchIngestService _search;
    private readonly string _container;

    public BlobDeletedEventGridTriggerFunction(
        ILoggerFactory loggerFactory,
        AzureAiSearchIngestService search)
    {
        _log = loggerFactory.CreateLogger<BlobDeletedEventGridTriggerFunction>();
        _search = search;
        _container = (Environment.GetEnvironmentVariable("BLOB_CONTAINER") ?? "fortianswer-docs").Trim();
    }

    [Function("BlobDeleted_EventGrid")]
    public async Task Run(
        [EventGridTrigger] EventGridEvent evt,
        CancellationToken ct)
    {
        // We only care about BlobDeleted
        if (!string.Equals(evt.EventType, "Microsoft.Storage.BlobDeleted", StringComparison.OrdinalIgnoreCase))
        {
            _log.LogInformation("Ignored eventType={EventType}", evt.EventType);
            return;
        }

        // Extract blob url from data.url
        string? url = TryGetUrl(evt.Data);
        if (string.IsNullOrWhiteSpace(url))
        {
            _log.LogWarning("BlobDeleted event missing data.url. subject={Subject}", evt.Subject);
            return;
        }

        // Convert to relative path inside the container
        // url looks like: https://{account}.blob.core.windows.net/{container}/{path}
        var path = ToRelativePath(url!, _container);
        if (string.IsNullOrWhiteSpace(path))
        {
            _log.LogWarning("Failed to compute relative path from url={Url} container={Container}", url, _container);
            return;
        }

        // Call your already working delete-by-path
        var deleted = await _search.DeleteByPathAsync(path, ct);

        _log.LogInformation("Auto-deleted vectors for path={Path}. deleted={Deleted}", path, deleted);
    }

    private static string? TryGetUrl(BinaryData data)
    {
        try
        {
            using var doc = JsonDocument.Parse(data.ToString());
            var root = doc.RootElement;

            // Event Grid Storage events commonly have data.url
            if (root.TryGetProperty("url", out var urlProp))
                return urlProp.GetString();

            // Fallbacks (just in case schema differs)
            if (root.TryGetProperty("blobUrl", out var bu))
                return bu.GetString();
        }
        catch { }

        return null;
    }

    private static string? ToRelativePath(string url, string container)
    {
        // Find "/{container}/" in the url
        var marker = "/" + container.Trim('/') + "/";
        var idx = url.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;

        var rel = url[(idx + marker.Length)..]; // after container/
        rel = rel.TrimStart('/');

        // We want exactly the same string as what you store in Search field `path`
        // In your system it's like: public/xxx.docx (relative path)
        // So rel should already be "public/xxx.docx"
        return rel;
    }
}