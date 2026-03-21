using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;

namespace FortiAnswer.Orchestrator.Middleware;

/// <summary>
/// Rejects any HTTP request that does not supply the correct x-api-key header.
/// Set API_KEY in environment variables / local.settings.json.
/// If API_KEY is not configured, the middleware is bypassed (safe for local dev without the var set).
/// </summary>
public sealed class ApiKeyMiddleware : IFunctionsWorkerMiddleware
{
    private const string HeaderName = "x-api-key";

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var expectedKey = Environment.GetEnvironmentVariable("API_KEY");

        // If no API_KEY is configured, skip enforcement (local dev convenience).
        if (string.IsNullOrWhiteSpace(expectedKey))
        {
            await next(context);
            return;
        }

        var request = await context.GetHttpRequestDataAsync();

        // Non-HTTP triggers (blob, event grid) are not protected.
        if (request is null)
        {
            await next(context);
            return;
        }

        var providedKey = request.Headers.TryGetValues(HeaderName, out var values)
            ? values.FirstOrDefault()
            : null;

        if (providedKey != expectedKey)
        {
            var response = request.CreateResponse(HttpStatusCode.Unauthorized);
            response.Headers.Add("Content-Type", "application/json");
            await response.WriteStringAsync(
                JsonSerializer.Serialize(new
                {
                    error = new { code = "Unauthorized", message = "Missing or invalid x-api-key header." }
                }));

            var invocationResult = context.GetInvocationResult();
            invocationResult.Value = response;
            return;
        }

        await next(context);
    }
}
