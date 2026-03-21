using System.Net;
using System.Text.Json;
using FortiAnswer.Orchestrator.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace FortiAnswer.Orchestrator.Functions;

/// <summary>
/// POST /api/feedback — record a thumbs-up or thumbs-down rating for a chat response.
///
/// Request body:
/// {
///   "requestId":  "ccf3fed0b5d545b8ae5993ffab2a952e",
///   "username":   "alice",
///   "rating":     "up",
///   "issueType":  "VPN",
///   "citations":  ["public/faq-vpn.docx", "public/vpn-setup-guide.pdf"]
/// }
/// </summary>
public sealed class FeedbackFunction
{
    private static readonly HashSet<string> ValidRatings =
        new(StringComparer.OrdinalIgnoreCase) { "up", "down" };

    private static readonly JsonSerializerOptions JsonIn =
        new() { PropertyNameCaseInsensitive = true };

    private readonly FeedbackTableService _feedback;

    public FeedbackFunction(FeedbackTableService feedback)
    {
        _feedback = feedback;
    }

    private record FeedbackRequest(
        string?   RequestId,
        string?   Username,
        string?   Rating,
        string?   IssueType,
        string[]? Citations);

    [Function("Feedback_Submit")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "feedback")] HttpRequestData req)
    {
        FeedbackRequest? input;
        try
        {
            input = await JsonSerializer.DeserializeAsync<FeedbackRequest>(req.Body, JsonIn);
        }
        catch
        {
            return await Error(req, HttpStatusCode.BadRequest, "InvalidJson", "Request body is not valid JSON.");
        }

        if (input is null || string.IsNullOrWhiteSpace(input.RequestId))
            return await Error(req, HttpStatusCode.BadRequest, "ValidationError", "Field 'requestId' is required.");

        if (string.IsNullOrWhiteSpace(input.Username))
            return await Error(req, HttpStatusCode.BadRequest, "ValidationError", "Field 'username' is required.");

        if (string.IsNullOrWhiteSpace(input.Rating) || !ValidRatings.Contains(input.Rating))
            return await Error(req, HttpStatusCode.BadRequest, "ValidationError", "Field 'rating' must be 'up' or 'down'.");

        await _feedback.RecordAsync(
            requestId:  input.RequestId.Trim(),
            username:   input.Username.Trim().ToLowerInvariant(),
            rating:     input.Rating.Trim().ToLowerInvariant(),
            issueType:  input.IssueType?.Trim(),
            citations:  input.Citations);

        var ok = req.CreateResponse(HttpStatusCode.Created);
        ok.Headers.Add("Content-Type", "application/json");
        await ok.WriteStringAsync("{\"recorded\":true}");
        return ok;
    }

    private static async Task<HttpResponseData> Error(
        HttpRequestData req, HttpStatusCode status, string code, string message)
    {
        var resp = req.CreateResponse(status);
        resp.Headers.Add("Content-Type", "application/json");
        await resp.WriteStringAsync(
            JsonSerializer.Serialize(new { error = new { code, message } },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        return resp;
    }
}
