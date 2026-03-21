using System.Net;
using System.Text.Json;
using System.Web;
using FortiAnswer.Orchestrator.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace FortiAnswer.Orchestrator.Functions;

/// <summary>
/// Feedback query endpoints — Admin only.
///
///   GET  /api/feedback/summary              Overall + per-issueType + per-document satisfaction rates
///   GET  /api/feedback/flagged              Low-rated responses pending admin review
///   PATCH /api/feedback/{requestId}/dismiss Mark a flagged response as reviewed
/// </summary>
public sealed class FeedbackQueryFunction
{
    private static readonly HashSet<string> AdminRoles =
        new(StringComparer.OrdinalIgnoreCase) { "admin", "agent" };

    private static readonly JsonSerializerOptions JsonOut =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly FeedbackTableService _feedback;

    public FeedbackQueryFunction(FeedbackTableService feedback)
    {
        _feedback = feedback;
    }

    // ── GET /api/feedback/summary ─────────────────────────────────────────────

    [Function("Feedback_Summary")]
    public async Task<HttpResponseData> Summary(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "feedback/summary")] HttpRequestData req)
    {
        var role = HttpUtility.ParseQueryString(req.Url.Query)["role"]?.Trim();

        if (string.IsNullOrWhiteSpace(role) || !AdminRoles.Contains(role))
            return await Forbidden(req);

        var summary = await _feedback.GetSummaryAsync();

        var ok = req.CreateResponse(HttpStatusCode.OK);
        ok.Headers.Add("Content-Type", "application/json");
        await ok.WriteStringAsync(JsonSerializer.Serialize(summary, JsonOut));
        return ok;
    }

    // ── GET /api/feedback/flagged ─────────────────────────────────────────────

    [Function("Feedback_Flagged")]
    public async Task<HttpResponseData> Flagged(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "feedback/flagged")] HttpRequestData req)
    {
        var role = HttpUtility.ParseQueryString(req.Url.Query)["role"]?.Trim();

        if (string.IsNullOrWhiteSpace(role) || !AdminRoles.Contains(role))
            return await Forbidden(req);

        var flagged = await _feedback.GetFlaggedAsync();

        var ok = req.CreateResponse(HttpStatusCode.OK);
        ok.Headers.Add("Content-Type", "application/json");
        await ok.WriteStringAsync(JsonSerializer.Serialize(
            new { total = flagged.Count, items = flagged }, JsonOut));
        return ok;
    }

    // ── PATCH /api/feedback/{requestId}/dismiss ───────────────────────────────

    [Function("Feedback_Dismiss")]
    public async Task<HttpResponseData> Dismiss(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "feedback/{requestId}/dismiss")] HttpRequestData req,
        string requestId)
    {
        var role = HttpUtility.ParseQueryString(req.Url.Query)["role"]?.Trim();

        if (string.IsNullOrWhiteSpace(role) || !AdminRoles.Contains(role))
            return await Forbidden(req);

        var found = await _feedback.DismissAsync(requestId.Trim());

        if (!found)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            notFound.Headers.Add("Content-Type", "application/json");
            await notFound.WriteStringAsync(
                "{\"error\":{\"code\":\"NotFound\",\"message\":\"Feedback record not found.\"}}");
            return notFound;
        }

        var ok = req.CreateResponse(HttpStatusCode.OK);
        ok.Headers.Add("Content-Type", "application/json");
        await ok.WriteStringAsync("{\"dismissed\":true}");
        return ok;
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static async Task<HttpResponseData> Forbidden(HttpRequestData req)
    {
        var resp = req.CreateResponse(HttpStatusCode.Forbidden);
        resp.Headers.Add("Content-Type", "application/json");
        await resp.WriteStringAsync(
            "{\"error\":{\"code\":\"Forbidden\",\"message\":\"Supply role=agent or role=admin.\"}}");
        return resp;
    }
}
