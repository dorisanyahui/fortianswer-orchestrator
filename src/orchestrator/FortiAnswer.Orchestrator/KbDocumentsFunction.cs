using System.Net;
using System.Text.Json;
using System.Web;
using FortiAnswer.Orchestrator.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace FortiAnswer.Orchestrator.Functions;

/// <summary>
/// GET /api/kb/documents?role=admin — list all documents currently indexed in the knowledge base.
///
/// Query params:
///   role           — must be "agent" or "admin"
///   classification — optional filter: public | internal | confidential | restricted
/// </summary>
public sealed class KbDocumentsFunction
{
    private static readonly HashSet<string> AllowedRoles =
        new(StringComparer.OrdinalIgnoreCase) { "agent", "admin" };

    private static readonly JsonSerializerOptions JsonOut =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly AzureAiSearchIngestService _search;
    private readonly ILogger<KbDocumentsFunction> _log;

    public KbDocumentsFunction(AzureAiSearchIngestService search, ILogger<KbDocumentsFunction> log)
    {
        _search = search;
        _log    = log;
    }

    [Function("Kb_Documents")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "kb/documents")] HttpRequestData req)
    {
        var qs   = HttpUtility.ParseQueryString(req.Url.Query);
        var role = qs["role"]?.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(role) || !AllowedRoles.Contains(role))
        {
            var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
            forbidden.Headers.Add("Content-Type", "application/json");
            await forbidden.WriteStringAsync(
                "{\"error\":{\"code\":\"Forbidden\",\"message\":\"Supply role=agent or role=admin.\"}}");
            return forbidden;
        }

        var classificationFilter = qs["classification"]?.Trim().ToLowerInvariant();

        try
        {
            var docs = await _search.ListDocumentsAsync(CancellationToken.None);

            if (!string.IsNullOrWhiteSpace(classificationFilter))
                docs = docs.Where(d =>
                    string.Equals(d.Classification, classificationFilter, StringComparison.OrdinalIgnoreCase))
                    .ToList();

            var ok = req.CreateResponse(HttpStatusCode.OK);
            ok.Headers.Add("Content-Type", "application/json");
            await ok.WriteStringAsync(JsonSerializer.Serialize(
                new { total = docs.Count, documents = docs }, JsonOut));
            return ok;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to list KB documents");
            var err = req.CreateResponse(HttpStatusCode.InternalServerError);
            err.Headers.Add("Content-Type", "application/json");
            await err.WriteStringAsync(
                "{\"error\":{\"code\":\"InternalError\",\"message\":\"Failed to retrieve KB document list.\"}}");
            return err;
        }
    }
}
