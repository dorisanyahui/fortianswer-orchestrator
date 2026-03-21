using System.Net;
using System.Text.Json;
using FortiAnswer.Orchestrator.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace FortiAnswer.Orchestrator.Functions;

/// <summary>
/// GET /api/health — returns overall status and per-dependency health checks.
///
/// Checks:
///   tableStorage — lightweight Table Storage read (TicketsTableService.PingAsync)
///   search       — lightweight Azure AI Search query (top=0)
///   groq         — verifies GROQ_API_KEY env var is configured (no actual API call to save cost)
///
/// Response when all healthy:
///   HTTP 200  { "status": "ok",      "checks": { "tableStorage": "ok", "search": "ok", "groq": "ok" } }
/// Response when any check fails:
///   HTTP 200  { "status": "degraded", "checks": { "tableStorage": "ok", "search": "fail", "groq": "ok" } }
/// </summary>
public class HealthFunction
{
    private readonly TicketsTableService           _tickets;
    private readonly AzureAiSearchIngestService    _search;
    private readonly ILogger<HealthFunction>       _logger;

    private static readonly JsonSerializerOptions JsonOut =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public HealthFunction(
        TicketsTableService           tickets,
        AzureAiSearchIngestService    search,
        ILogger<HealthFunction>       logger)
    {
        _tickets = tickets;
        _search  = search;
        _logger  = logger;
    }

    [Function("health")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequestData req)
    {
        var correlationId = GetOrCreateCorrelationId(req);

        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["correlationId"] = correlationId,
            ["function"]      = "health"
        }))
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            var tableTask  = _tickets.PingAsync(cts.Token);
            var searchTask = _search.PingAsync(cts.Token);
            var groqOk     = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GROQ_API_KEY"));

            bool tableOk, searchOk;
            try
            {
                await Task.WhenAll(tableTask, searchTask);
                tableOk  = tableTask.Result;
                searchOk = searchTask.Result;
            }
            catch
            {
                tableOk  = tableTask.IsCompletedSuccessfully && tableTask.Result;
                searchOk = searchTask.IsCompletedSuccessfully && searchTask.Result;
            }

            var checks = new
            {
                tableStorage = tableOk  ? "ok" : "fail",
                search       = searchOk ? "ok" : "fail",
                groq         = groqOk   ? "ok" : "missing"
            };

            var allOk  = tableOk && searchOk && groqOk;
            var status = allOk ? "ok" : "degraded";

            _logger.LogInformation("health.check status={Status} tableStorage={Table} search={Search} groq={Groq}",
                status, checks.tableStorage, checks.search, checks.groq);

            var res = req.CreateResponse(HttpStatusCode.OK);
            res.Headers.Add("Content-Type", "application/json");
            res.Headers.Add("x-correlation-id", correlationId);
            await res.WriteStringAsync(JsonSerializer.Serialize(
                new { status, checks, timestamp = DateTimeOffset.UtcNow }, JsonOut));
            return res;
        }
    }

    private static string GetOrCreateCorrelationId(HttpRequestData req)
    {
        if (req.Headers.TryGetValues("x-correlation-id", out var values))
        {
            var existing = values?.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(existing))
                return existing.Trim();
        }
        return Guid.NewGuid().ToString("N");
    }
}
