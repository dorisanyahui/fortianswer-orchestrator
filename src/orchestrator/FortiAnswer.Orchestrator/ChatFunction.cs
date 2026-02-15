using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

using FortiAnswer.Orchestrator.Models;
using FortiAnswer.Orchestrator.Services;

namespace FortiAnswer.Orchestrator;

public sealed class ChatFunction
{
    private readonly ILogger _log;
    private readonly RetrievalService _retrieval;
    private readonly WebSearchService _webSearch;
    private readonly PromptBuilder _promptBuilder;
    private readonly GroqClient _groq;

    private static readonly JsonSerializerOptions JsonIn = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions JsonOut = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public ChatFunction(
        ILoggerFactory loggerFactory,
        RetrievalService retrieval,
        WebSearchService webSearch,
        PromptBuilder promptBuilder,
        GroqClient groq)
    {
        _log = loggerFactory.CreateLogger<ChatFunction>();
        _retrieval = retrieval;
        _webSearch = webSearch;
        _promptBuilder = promptBuilder;
        _groq = groq;
    }

    [Function("chat")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "chat")] HttpRequestData req)
    {
        var correlationId = GetOrCreateCorrelationId(req);

        try
        {
            // 1) Read body
            var bodyText = await new StreamReader(req.Body).ReadToEndAsync();
            if (string.IsNullOrWhiteSpace(bodyText))
                return await BadRequest(req, correlationId, "MissingBody", "Request body is required.");

            ChatRequest? body;
            try
            {
                body = JsonSerializer.Deserialize<ChatRequest>(bodyText, JsonIn);
            }
            catch
            {
                return await BadRequest(req, correlationId, "InvalidJson", "Invalid JSON body.");
            }

            // 2) Validate
            var message = body?.Message?.Trim();
            if (string.IsNullOrWhiteSpace(message))
                return await BadRequest(req, correlationId, "ValidationError", "Field 'message' is required.", new { field = "message" });

            var requestType = (body?.RequestType ?? Environment.GetEnvironmentVariable("DATA_BOUNDARY_DEFAULT") ?? "Public").Trim();
            var userRole = (body?.UserRole ?? "User").Trim();
            var userGroup = body?.UserGroup;
            var conversationId = body?.ConversationId;

            // ✅ Step1: TopK from env (safe defaults + clamp)
            var internalTopK = ReadTopK("INTERNAL_TOPK", defaultValue: 2, min: 1, max: 10);
            var webTopK = ReadTopK("WEB_TOPK", defaultValue: 3, min: 1, max: 10);

            var actionHints = new List<string>
            {
                $"cfg:internalTopK={internalTopK}",
                $"cfg:webTopK={webTopK}"
            };


            // 3) Internal retrieval
            var internalBundle = await _retrieval.RetrieveAsync(
                question: message!,
                requestType: requestType,
                userGroup: userGroup,
                topK: internalTopK,
                correlationId: correlationId
            );

            if (internalBundle.Citations.Count > 0) actionHints.Add("used:internal_search");
            else actionHints.Add("used:internal_search_empty");

            // 4) Optional web search (Public + internal empty)
            WebSearchBundle? webBundle = null;
            if (string.Equals(requestType, "Public", StringComparison.OrdinalIgnoreCase)
                && internalBundle.Citations.Count == 0)
            {
                webBundle = await _webSearch.SearchAsync(
                    query: message!,
                    topK: webTopK,
                    correlationId: correlationId
                );

                actionHints.Add("used:web_search");
            }
            else
            {
                actionHints.Add("next:optional_web_search");
            }

            // 5) Build prompt (✅ matches your PromptBuilder.cs signature)
            var prompt = _promptBuilder.Build(
                userMessage: message!,
                requestType: requestType,
                userRole: userRole,
                userGroup: userGroup,
                conversationId: conversationId,
                internalContext: internalBundle.Context,
                webContext: webBundle?.Context
            );

            // 6) LLM generate (Groq)
            var answer = await _groq.GenerateAsync(prompt, correlationId);

            // 7) Merge citations
            var mergedCitations = new List<Citation>();
            mergedCitations.AddRange(internalBundle.Citations);
            if (webBundle is not null) mergedCitations.AddRange(webBundle.Citations);

            // 8) Response
            var resp = req.CreateResponse(HttpStatusCode.OK);
            resp.Headers.Add("Content-Type", "application/json");
            resp.Headers.Add("x-correlation-id", correlationId);

            var payload = new ChatResponse
            {
                Answer = answer ?? "",
                Citations = mergedCitations,
                ActionHints = actionHints,
                RequestId = correlationId,
                Escalation = new EscalationInfo { ShouldEscalate = false, Reason = "" },
                Mode = new ModeInfo
                {
                    Retrieval = Environment.GetEnvironmentVariable("RETRIEVAL_MODE") ?? "stub",
                    Llm = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GROQ_API_KEY")) ? "stub" : "groq"
                }
            };

            await resp.WriteStringAsync(JsonSerializer.Serialize(payload, JsonOut));
            return resp;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ChatFunction failed. correlationId={correlationId}", correlationId);
            return await InternalError(req, correlationId, "InternalError", "Unexpected error.");
        }
    }

    // ---------------- Helpers ----------------

    private static string GetOrCreateCorrelationId(HttpRequestData req)
    {
        if (req.Headers.TryGetValues("x-correlation-id", out var vals))
        {
            var v = vals?.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(v)) return v!;
        }
        return Guid.NewGuid().ToString("N");
    }

    private static int ReadTopK(string envName, int defaultValue, int min = 1, int max = 10)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        if (int.TryParse(raw, out var v))
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }
        return defaultValue;
    }

    private static async Task<HttpResponseData> BadRequest(
        HttpRequestData req,
        string requestId,
        string code,
        string message,
        object? details = null)
    {
        var resp = req.CreateResponse(HttpStatusCode.BadRequest);
        resp.Headers.Add("Content-Type", "application/json");
        resp.Headers.Add("x-correlation-id", requestId);

        var payload = new ErrorResponse
        {
            RequestId = requestId,
            Error = new ErrorInfo
            {
                Code = code,
                Message = message,
                Details = details
            }
        };

        await resp.WriteStringAsync(JsonSerializer.Serialize(payload, JsonOut));
        return resp;
    }

    private static async Task<HttpResponseData> InternalError(
        HttpRequestData req,
        string requestId,
        string code,
        string message)
    {
        var resp = req.CreateResponse(HttpStatusCode.InternalServerError);
        resp.Headers.Add("Content-Type", "application/json");
        resp.Headers.Add("x-correlation-id", requestId);

        var payload = new ErrorResponse
        {
            RequestId = requestId,
            Error = new ErrorInfo
            {
                Code = code,
                Message = message
            }
        };

        await resp.WriteStringAsync(JsonSerializer.Serialize(payload, JsonOut));
        return resp;
    }
}
