using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using FortiAnswer.Orchestrator.Services;

namespace FortiAnswer.Orchestrator.Functions;

public sealed class DebugFunction
{
    private readonly ILogger _log;
    private readonly TableStorageService _tables;

    public DebugFunction(ILoggerFactory loggerFactory, TableStorageService tables)
    {
        _log = loggerFactory.CreateLogger<DebugFunction>();
        _tables = tables;
    }

    [Function("GetDebugByRequestId")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "debug/{requestId}")] HttpRequestData req,
        string requestId)
    {
        // Header gate (internal only)
        var expected = (Environment.GetEnvironmentVariable("DEBUG_VIEW_KEY") ?? "").Trim();
        if (string.IsNullOrWhiteSpace(expected))
        {
            var mis = req.CreateResponse(HttpStatusCode.InternalServerError);
            await mis.WriteAsJsonAsync(new { ok = false, message = "DEBUG_VIEW_KEY not configured." });
            return mis;
        }

        if (!req.Headers.TryGetValues("X-Debug-Key", out var vals) ||
            !string.Equals(vals?.FirstOrDefault()?.Trim(), expected, StringComparison.Ordinal))
        {
            var forb = req.CreateResponse(HttpStatusCode.Forbidden);
            await forb.WriteAsJsonAsync(new { ok = false, message = "Forbidden" });
            return forb;
        }

        var result = await _tables.GetDebugByRequestIdAsync(requestId);
        if (result is null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { ok = false, message = "No debug found for requestId", requestId });
            return notFound;
        }

        var ok = req.CreateResponse(HttpStatusCode.OK);
        await ok.WriteAsJsonAsync(new
        {
            ok = true,
            requestId,
            meta = new
            {
                result.CreatedAtUtc,
                result.ConversationId,
                result.Outcome,
                result.Role,
                result.DataBoundary,
                result.RequestType,
                result.UsedRetrieval,
                result.TopScore,
                result.TicketId,
                result.LatencyMs,
                result.PartitionKey,
                result.RowKey
            },
            debugJson = result.DebugJson
        });

        _log.LogInformation("Returned debug for requestId={RequestId}", requestId);
        return ok;
    }
}