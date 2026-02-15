using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace FortiAnswer.Orchestrator.Functions;

public class HealthFunction
{
    private readonly ILogger<HealthFunction> _logger;

    public HealthFunction(ILogger<HealthFunction> logger)
    {
        _logger = logger;
    }

    [Function("health")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequestData req)
    {
        var correlationId = GetOrCreateCorrelationId(req);

        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["correlationId"] = correlationId,
            ["function"] = "health",
            ["method"] = req.Method,
            ["path"] = req.Url?.AbsolutePath ?? ""
        }))
        {
            _logger.LogInformation("health.ping");

            var res = req.CreateResponse(HttpStatusCode.OK);
            res.Headers.Add("Content-Type", "application/json");
            res.Headers.Add("x-correlation-id", correlationId);

            await res.WriteStringAsync("""{"status":"ok"}""");
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
