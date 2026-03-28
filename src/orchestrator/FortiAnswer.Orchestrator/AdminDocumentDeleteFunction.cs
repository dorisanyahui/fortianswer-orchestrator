using System.Net;
using System.Text.Json;
using System.Web;
using Azure.Storage.Blobs;
using FortiAnswer.Orchestrator.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace FortiAnswer.Orchestrator.Functions;

/// <summary>
/// DELETE /api/documents/delete — remove a document from blob storage AND the search index.
///
/// This is the correct single-step delete for Admin use. Deleting only the blob
/// (e.g. via Azure Portal) will NOT remove it from the search index; the KB list
/// will still show the document until the index is also cleaned up.
///
/// Query params:
///   role — must be "admin"
///   path — full blob path, e.g. "public/vpn-guide.docx"
/// </summary>
public sealed class AdminDocumentDeleteFunction
{
    private static readonly JsonSerializerOptions JsonOut =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly BlobContainerClient _container;
    private readonly AzureAiSearchIngestService _search;
    private readonly ILogger<AdminDocumentDeleteFunction> _log;

    public AdminDocumentDeleteFunction(
        AzureAiSearchIngestService search,
        ILogger<AdminDocumentDeleteFunction> log)
    {
        _search = search;
        _log    = log;

        var conn      = Environment.GetEnvironmentVariable("BLOB_CONNECTION")
            ?? throw new InvalidOperationException("BLOB_CONNECTION missing");
        var container = Environment.GetEnvironmentVariable("BLOB_CONTAINER")
                     ?? Environment.GetEnvironmentVariable("INGEST_CONTAINER")
                     ?? "fortianswer-docs";

        _container = new BlobContainerClient(conn, container);
    }

    [Function("Admin_DocumentDelete")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "documents/delete")] HttpRequestData req,
        CancellationToken ct)
    {
        var qs   = HttpUtility.ParseQueryString(req.Url.Query);
        var role = qs["role"]?.Trim().ToLowerInvariant();

        if (role != "admin")
            return await ErrorAsync(req, HttpStatusCode.Forbidden,
                "Forbidden", "Only admins can delete documents. Supply role=admin.");

        var path = qs["path"]?.Trim();
        if (string.IsNullOrWhiteSpace(path))
            return await ErrorAsync(req, HttpStatusCode.BadRequest,
                "BadRequest", "Supply ?path= with the full blob path, e.g. public/vpn-guide.docx");

        var blobDeleted   = false;
        var chunksDeleted = 0;

        // Step 1: delete blob (best-effort — blob may already be gone)
        try
        {
            var blobClient = _container.GetBlobClient(path);
            var result     = await blobClient.DeleteIfExistsAsync(cancellationToken: ct);
            blobDeleted    = result.Value;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "AdminDocumentDelete: blob delete failed for path={Path} (continuing to index cleanup)", path);
        }

        // Step 2: remove chunks from search index
        try
        {
            chunksDeleted = await _search.DeleteByPathAsync(path, ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "AdminDocumentDelete: index cleanup failed for path={Path}", path);
            return await ErrorAsync(req, HttpStatusCode.InternalServerError,
                "InternalError", $"Blob deleted={blobDeleted} but index cleanup failed: {ex.Message}");
        }

        _log.LogInformation(
            "AdminDocumentDelete: path={Path} blobDeleted={BlobDeleted} chunksDeleted={ChunksDeleted}",
            path, blobDeleted, chunksDeleted);

        var ok = req.CreateResponse(HttpStatusCode.OK);
        ok.Headers.Add("Content-Type", "application/json");
        await ok.WriteStringAsync(JsonSerializer.Serialize(new
        {
            ok       = true,
            path,
            blobDeleted,
            chunksDeleted
        }, JsonOut));
        return ok;
    }

    private static async Task<HttpResponseData> ErrorAsync(
        HttpRequestData req, HttpStatusCode code, string errorCode, string message)
    {
        var res = req.CreateResponse(code);
        res.Headers.Add("Content-Type", "application/json");
        await res.WriteStringAsync(
            JsonSerializer.Serialize(new { error = new { code = errorCode, message } }));
        return res;
    }
}
