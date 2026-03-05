using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using FortiAnswer.Orchestrator.Services;

namespace FortiAnswer.Orchestrator.Functions;

public sealed class DeleteVectorsFunction
{
    private readonly ILogger _log;
    private readonly AzureAiSearchIngestService _search;

    public DeleteVectorsFunction(ILoggerFactory loggerFactory, AzureAiSearchIngestService search)
    {
        _log = loggerFactory.CreateLogger<DeleteVectorsFunction>();
        _search = search;
    }

    [Function("DeleteVectorsByPath")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "ops/vectors")] HttpRequestData req,
        CancellationToken ct)
    {
        // Gate with X-Debug-Key (same pattern as your debug endpoint)
        var expected = (Environment.GetEnvironmentVariable("DEBUG_VIEW_KEY") ?? "").Trim();
        if (string.IsNullOrWhiteSpace(expected))
        {
            var mis = req.CreateResponse(HttpStatusCode.InternalServerError);
            await mis.WriteAsJsonAsync(new { ok = false, message = "DEBUG_VIEW_KEY not configured." }, cancellationToken: ct);
            return mis;
        }

        if (!req.Headers.TryGetValues("X-Debug-Key", out var vals) ||
            !string.Equals(vals?.FirstOrDefault()?.Trim(), expected, StringComparison.Ordinal))
        {
            var forb = req.CreateResponse(HttpStatusCode.Forbidden);
            await forb.WriteAsJsonAsync(new { ok = false, message = "Forbidden" }, cancellationToken: ct);
            return forb;
        }

        // Query: ?path=public/xxx.docx
        var q = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var path = (q.Get("path") ?? "").Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new
            {
                ok = false,
                message = "Missing path. Example: /api/admin/vectors?path=public/FAQ-PUBLIC-VPN-TROUBLESHOOTING-20260217.docx"
            }, cancellationToken: ct);
            return bad;
        }

        var deleted = await _search.DeleteByPathAsync(path, ct);

        var ok = req.CreateResponse(HttpStatusCode.OK);
        await ok.WriteAsJsonAsync(new { ok = true, path, deleted }, cancellationToken: ct);

        _log.LogInformation("Deleted vectors by path. path={Path} deleted={Deleted}", path, deleted);
        return ok;
    }
}