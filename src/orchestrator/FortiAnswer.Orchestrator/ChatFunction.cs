using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using FortiAnswer.Orchestrator.Models;

namespace FortiAnswer.Orchestrator.Functions;

public class ChatFunction
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    [Function("chat")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "chat")] HttpRequestData req)
    {
        var requestId = Guid.NewGuid().ToString();

        ChatRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<ChatRequest>(req.Body, JsonOptions);
        }
        catch
        {
            return CreateError(req, HttpStatusCode.BadRequest, requestId, "Invalid JSON payload.");
        }

        if (body is null)
            return CreateError(req, HttpStatusCode.BadRequest, requestId, "Missing request body.");

        if (string.IsNullOrWhiteSpace(body.Message))
            return CreateError(req, HttpStatusCode.BadRequest, requestId, "Field 'message' is required.");

        if (string.IsNullOrWhiteSpace(body.RequestType))
            return CreateError(req, HttpStatusCode.BadRequest, requestId, "Field 'requestType' is required.");

        if (string.IsNullOrWhiteSpace(body.UserRole))
            return CreateError(req, HttpStatusCode.BadRequest, requestId, "Field 'userRole' is required.");

        // Placeholder logic (retrieval + LLM will be added in later tasks)
        var response = new ChatResponse
        {
            Answer = $"[placeholder] Received: \"{body.Message}\" (requestType={body.RequestType}, role={body.UserRole})",
            RequestId = requestId,
            Citations = new(),
            ActionHints = new List<string> { "next:integrate_retrieval", "next:integrate_llm" },
            Escalation = new EscalationInfo { ShouldEscalate = false, Reason = "" }
        };

        var res = req.CreateResponse(HttpStatusCode.OK);
        res.Headers.Add("Content-Type", "application/json");
        await res.WriteStringAsync(JsonSerializer.Serialize(response, JsonOptions));
        return res;
    }

    private static HttpResponseData CreateError(HttpRequestData req, HttpStatusCode status, string requestId, string message)
    {
        var res = req.CreateResponse(status);
        res.Headers.Add("Content-Type", "application/json");

        var payload = new { requestId, error = message };
        res.WriteString(JsonSerializer.Serialize(payload, JsonOptions));
        return res;
    }
}
