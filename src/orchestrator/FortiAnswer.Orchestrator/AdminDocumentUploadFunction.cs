using System.Net;
using System.Text.Json;
using System.Web;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace FortiAnswer.Orchestrator.Functions;

/// <summary>
/// POST /api/documents/upload — upload a document to the knowledge base blob container.
///
/// The blob upload triggers BlobIngestTriggerFunction automatically, so no manual
/// ingestion step is required after this call.
///
/// Query params:
///   role           — must be "admin"
///   filename       — required, e.g. "vpn-guide.docx" (must end in .pdf, .docx, .txt, or .md)
///   classification — optional, defaults to "public"
///                    one of: public | internal | confidential | restricted
///
/// Request body: raw binary file content
/// Content-Type:  application/octet-stream
///
/// Response 201:
///   { "blobPath": "internal/vpn-guide.docx", "filename": "vpn-guide.docx",
///     "classification": "internal", "message": "Upload successful. Ingestion will begin shortly." }
/// </summary>
public sealed class AdminDocumentUploadFunction
{
    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".docx", ".txt", ".md" };

    private static readonly HashSet<string> AllowedClassifications =
        new(StringComparer.OrdinalIgnoreCase) { "public", "internal", "confidential", "restricted" };

    private static readonly JsonSerializerOptions JsonOut =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly BlobContainerClient _container;
    private readonly ILogger<AdminDocumentUploadFunction> _log;

    public AdminDocumentUploadFunction(ILogger<AdminDocumentUploadFunction> log)
    {
        _log = log;

        var conn      = Environment.GetEnvironmentVariable("BLOB_CONNECTION")
            ?? throw new InvalidOperationException("BLOB_CONNECTION missing");
        var container = Environment.GetEnvironmentVariable("BLOB_CONTAINER")
                     ?? Environment.GetEnvironmentVariable("INGEST_CONTAINER")
                     ?? "fortianswer-docs";

        _container = new BlobContainerClient(conn, container);
    }

    [Function("Admin_DocumentUpload")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "documents/upload")] HttpRequestData req)
    {
        var qs   = HttpUtility.ParseQueryString(req.Url.Query);
        var role = qs["role"]?.Trim().ToLowerInvariant();

        if (role != "admin")
            return await ErrorAsync(req, HttpStatusCode.Forbidden,
                "Forbidden", "Only admins can upload documents. Supply role=admin.");

        var filename = qs["filename"]?.Trim();
        if (string.IsNullOrWhiteSpace(filename))
            return await ErrorAsync(req, HttpStatusCode.BadRequest,
                "BadRequest", "Supply ?filename= with a .pdf, .docx, .txt, or .md filename.");

        var ext = Path.GetExtension(filename);
        if (!AllowedExtensions.Contains(ext))
            return await ErrorAsync(req, HttpStatusCode.BadRequest,
                "BadRequest", $"Unsupported file type '{ext}'. Allowed: .pdf, .docx, .txt, .md");

        filename = Path.GetFileName(filename);

        var classification = qs["classification"]?.Trim().ToLowerInvariant() ?? "public";
        if (!AllowedClassifications.Contains(classification))
            return await ErrorAsync(req, HttpStatusCode.BadRequest,
                "BadRequest", $"Invalid classification '{classification}'. Use: public | internal | confidential | restricted");

        var blobPath = $"{classification}/{filename}";

        try
        {
            await _container.CreateIfNotExistsAsync();
            var blobClient = _container.GetBlobClient(blobPath);

            await using var body = req.Body;
            await blobClient.UploadAsync(body, overwrite: true);

            _log.LogInformation("AdminDocumentUpload: uploaded blobPath={BlobPath}", blobPath);

            var created = req.CreateResponse(HttpStatusCode.Created);
            created.Headers.Add("Content-Type", "application/json");
            await created.WriteStringAsync(JsonSerializer.Serialize(new
            {
                blobPath,
                filename,
                classification,
                message = "Upload successful. Ingestion will begin shortly."
            }, JsonOut));
            return created;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "AdminDocumentUpload: failed to upload blobPath={BlobPath}", blobPath);
            return await ErrorAsync(req, HttpStatusCode.InternalServerError,
                "InternalError", "Failed to upload document.");
        }
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
