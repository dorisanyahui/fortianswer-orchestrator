using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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

    // ✅ authLevel changed to Anonymous
    [Function("chat")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "chat")] HttpRequestData req)
    {
        // (3) Server-generated request id; keep client correlation id separately
        var clientCorrelationId = GetClientCorrelationId(req);
        var requestId = Guid.NewGuid().ToString("N");

        try
        {
            // 1) Read body
            var bodyText = await new StreamReader(req.Body).ReadToEndAsync();
            if (string.IsNullOrWhiteSpace(bodyText))
                return await BadRequest(req, requestId, clientCorrelationId, "MissingBody", "Request body is required.");

            ChatRequest? body;
            try
            {
                body = JsonSerializer.Deserialize<ChatRequest>(bodyText, JsonIn);
            }
            catch
            {
                return await BadRequest(req, requestId, clientCorrelationId, "InvalidJson", "Invalid JSON body.");
            }

            // 2) Validate
            var message = body?.Message?.Trim();
            if (string.IsNullOrWhiteSpace(message))
                return await BadRequest(req, requestId, clientCorrelationId, "ValidationError", "Field 'message' is required.", new { field = "message" });

            var requestType = (body?.RequestType ?? Environment.GetEnvironmentVariable("DATA_BOUNDARY_DEFAULT") ?? "Public").Trim();
            var userRole = (body?.UserRole ?? "User").Trim();
            var userGroup = body?.UserGroup;
            var conversationId = body?.ConversationId;

            // TopK config
            var internalTopK = ReadTopK("INTERNAL_TOPK", 2, 1, 10);
            var webTopK = ReadTopK("WEB_TOPK", 3, 1, 10);

            var actionHints = new List<string>
            {
                $"cfg:internalTopK={internalTopK}",
                $"cfg:webTopK={webTopK}",
                $"serverRequestId={requestId}"
            };

            if (!string.IsNullOrWhiteSpace(clientCorrelationId))
                actionHints.Add($"clientCorrelationId={clientCorrelationId}");

            // 3) Internal retrieval (object, not dynamic)
            object internalBundle = await _retrieval.RetrieveAsync(
                question: message!,
                requestType: requestType,
                userGroup: userGroup,
                topK: internalTopK,
                correlationId: requestId
            );

            var internalContext = ExtractString(internalBundle, "Context") ?? "";
            var internalCitationsRaw = ExtractCitations(internalBundle, "Citations");

            actionHints.Add(internalCitationsRaw.Count > 0 ? "used:internal_search" : "used:internal_search_empty");

            // 4) Decide if internal evidence is insufficient/off-topic
            double minScore = GetDoubleEnv("SEARCH_MIN_SCORE", 0.01);
            double bestScore = GetBestScore(internalCitationsRaw);

            var internalTextForOverlap =
                internalContext + "\n\n" +
                string.Join("\n\n", internalCitationsRaw.ConvertAll(c => c.Snippet ?? ""));

            bool noEvidence = internalCitationsRaw.Count == 0;
            bool weakEvidence = !noEvidence && bestScore < minScore;
            bool lowOverlap = IsLowOverlap(message!, internalTextForOverlap, minOverlap: 2);

            actionHints.Add($"internal:bestScore={bestScore:0.###}");
            actionHints.Add($"internal:minScore={minScore:0.###}");
            actionHints.Add($"internal:lowOverlap={(lowOverlap ? "true" : "false")}");

            bool needWeb = noEvidence || weakEvidence || lowOverlap;

            // (1) Filter off-topic internal citations
            var internalCitationsFiltered = (lowOverlap || weakEvidence)
                ? new List<Citation>()
                : internalCitationsRaw;

            if (lowOverlap || weakEvidence)
                actionHints.Add("internal:citations_filtered=true");

            // 5) Web enablement
            bool isPublic = string.Equals(requestType, "Public", StringComparison.OrdinalIgnoreCase);
            var webMode = (Environment.GetEnvironmentVariable("WEBSEARCH_MODE") ?? "off").Trim().ToLowerInvariant();
            bool webEnabled = isPublic && webMode == "tavily" && IsTavilyConfigured();

            object? webBundle = null;
            string? webContext = null;

            var mergedCitations = new List<Citation>(internalCitationsFiltered);

            // Step 1: need web but not confirmed => ask
            if (needWeb && webEnabled && body?.ConfirmWebSearch != true)
            {
                actionHints.Add("next:ask_user_for_web_search");

                var token = MakeWebConfirmToken(message!, requestId);

                var payloadAsk = new ChatResponse
                {
                    Answer =
                        "Internal knowledge base did not return enough relevant evidence to answer this question.\n" +
                        "Would you like me to run a Web Search (Tavily) to supplement the answer?\n\n" +
                        "If yes, resend the request with confirmWebSearch=true and include webSearchToken.",
                    Citations = mergedCitations,
                    ActionHints = actionHints,
                    RequestId = requestId,
                    NeedsWebConfirmation = true,
                    WebSearchToken = token,
                    Escalation = new EscalationInfo { ShouldEscalate = false, Reason = "" },
                    Mode = new ModeInfo
                    {
                        Retrieval = Environment.GetEnvironmentVariable("RETRIEVAL_MODE") ?? "stub",
                        Llm = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GROQ_API_KEY")) ? "stub" : "groq"
                    }
                };

                return await Ok(req, requestId, clientCorrelationId, payloadAsk);
            }

            // Step 2: confirmed => validate token => run web search
            if (needWeb && webEnabled && body?.ConfirmWebSearch == true)
            {
                if (string.IsNullOrWhiteSpace(body.WebSearchToken))
                    return await BadRequest(req, requestId, clientCorrelationId, "ValidationError",
                        "Field 'webSearchToken' is required when confirmWebSearch=true.",
                        new { field = "webSearchToken" });

                if (!ValidateWebConfirmToken(body.WebSearchToken!, message!))
                    return await BadRequest(req, requestId, clientCorrelationId, "ValidationError",
                        "Invalid or expired webSearchToken.",
                        new { field = "webSearchToken" });

                webBundle = await _webSearch.SearchAsync(
                    query: message!,
                    topK: webTopK,
                    correlationId: requestId
                );

                webContext = ExtractString(webBundle, "Context");
                var webCitations = ExtractCitations(webBundle, "Citations");

                if (webCitations.Count > 0)
                    mergedCitations.AddRange(webCitations);

                actionHints.Add("used:web_search");
            }
            else
            {
                actionHints.Add(needWeb ? "web_disabled_or_not_allowed" : "next:optional_web_search");
            }

            // 6) Build prompt (plus guardrails)
            var basePrompt = _promptBuilder.Build(
                userMessage: message!,
                requestType: requestType,
                userRole: userRole,
                userGroup: userGroup,
                conversationId: conversationId,
                internalContext: internalContext,
                webContext: webContext
            );

            // (2) Add source-grounding + timeline consistency rules
            var guardrails =
                "\n\n=== RESPONSE RULES (IMPORTANT) ===\n" +
                "1) Use only facts supported by the provided citations (especially dates, versions, affected products, and mitigations).\n" +
                "2) If a detail is not supported by citations, say \"Not confirmed by sources\" and do not guess.\n" +
                "3) If you include a timeline, label each entry as one of: discovered / reported / removed / patched, and keep the timeline internally consistent.\n" +
                "4) Prefer concise, actionable mitigations.\n" +
                "=== END RULES ===\n";

            var prompt = basePrompt + guardrails;

            // 7) Groq generate
            var answer = await _groq.GenerateAsync(prompt, requestId) ?? "";

            // 8) Response
            var payload = new ChatResponse
            {
                Answer = answer,
                Citations = mergedCitations,
                ActionHints = actionHints,
                RequestId = requestId,
                NeedsWebConfirmation = false,
                WebSearchToken = null,
                Escalation = new EscalationInfo { ShouldEscalate = false, Reason = "" },
                Mode = new ModeInfo
                {
                    Retrieval = Environment.GetEnvironmentVariable("RETRIEVAL_MODE") ?? "stub",
                    Llm = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GROQ_API_KEY")) ? "stub" : "groq"
                }
            };

            return await Ok(req, requestId, clientCorrelationId, payload);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ChatFunction failed. requestId={requestId} clientCorrelationId={clientCorrelationId}", requestId, clientCorrelationId);
            return await InternalError(req, requestId, clientCorrelationId, "InternalError", "Unexpected error.");
        }
    }

    // ---------------- Reflection helpers (no dynamic LINQ) ----------------

    private static string? ExtractString(object? obj, string propertyName)
    {
        if (obj is null) return null;
        try
        {
            var prop = obj.GetType().GetProperty(propertyName);
            return prop?.GetValue(obj)?.ToString();
        }
        catch { return null; }
    }

    private static List<Citation> ExtractCitations(object? obj, string propertyName)
    {
        var list = new List<Citation>();
        if (obj is null) return list;

        try
        {
            var prop = obj.GetType().GetProperty(propertyName);
            if (prop is null) return list;

            var raw = prop.GetValue(obj);
            if (raw is IEnumerable<Citation> typed)
                return new List<Citation>(typed);

            if (raw is System.Collections.IEnumerable any)
            {
                foreach (var item in any)
                {
                    if (item is null) continue;
                    var t = item.GetType();

                    string? title = t.GetProperty("Title")?.GetValue(item)?.ToString();
                    string? urlOrId =
                        t.GetProperty("UrlOrId")?.GetValue(item)?.ToString()
                        ?? t.GetProperty("Url")?.GetValue(item)?.ToString()
                        ?? t.GetProperty("Id")?.GetValue(item)?.ToString();

                    string? snippet = t.GetProperty("Snippet")?.GetValue(item)?.ToString();

                    list.Add(new Citation { Title = title, UrlOrId = urlOrId, Snippet = snippet });
                }
            }
        }
        catch
        {
            // ignore and return empty
        }

        return list;
    }

    // ---------------- HTTP helpers ----------------

    private static async Task<HttpResponseData> Ok(HttpRequestData req, string requestId, string? clientCorrelationId, object payload)
    {
        var resp = req.CreateResponse(HttpStatusCode.OK);
        resp.Headers.Add("Content-Type", "application/json");
        resp.Headers.Add("x-correlation-id", requestId);
        if (!string.IsNullOrWhiteSpace(clientCorrelationId))
            resp.Headers.Add("x-client-correlation-id", clientCorrelationId);

        await resp.WriteStringAsync(JsonSerializer.Serialize(payload, JsonOut));
        return resp;
    }

    private static async Task<HttpResponseData> BadRequest(
        HttpRequestData req,
        string requestId,
        string? clientCorrelationId,
        string code,
        string message,
        object? details = null)
    {
        var resp = req.CreateResponse(HttpStatusCode.BadRequest);
        resp.Headers.Add("Content-Type", "application/json");
        resp.Headers.Add("x-correlation-id", requestId);
        if (!string.IsNullOrWhiteSpace(clientCorrelationId))
            resp.Headers.Add("x-client-correlation-id", clientCorrelationId);

        var payload = new ErrorResponse
        {
            RequestId = requestId,
            Error = new ErrorInfo { Code = code, Message = message, Details = details }
        };

        await resp.WriteStringAsync(JsonSerializer.Serialize(payload, JsonOut));
        return resp;
    }

    private static async Task<HttpResponseData> InternalError(
        HttpRequestData req,
        string requestId,
        string? clientCorrelationId,
        string code,
        string message)
    {
        var resp = req.CreateResponse(HttpStatusCode.InternalServerError);
        resp.Headers.Add("Content-Type", "application/json");
        resp.Headers.Add("x-correlation-id", requestId);
        if (!string.IsNullOrWhiteSpace(clientCorrelationId))
            resp.Headers.Add("x-client-correlation-id", clientCorrelationId);

        var payload = new ErrorResponse
        {
            RequestId = requestId,
            Error = new ErrorInfo { Code = code, Message = message }
        };

        await resp.WriteStringAsync(JsonSerializer.Serialize(payload, JsonOut));
        return resp;
    }

    // ---------------- Correlation / env ----------------

    private static string? GetClientCorrelationId(HttpRequestData req)
    {
        if (req.Headers.TryGetValues("x-correlation-id", out var vals))
        {
            var v = vals?.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(v)) return v!;
        }
        return null;
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

    private static double GetDoubleEnv(string name, double defaultValue)
    {
        var v = (Environment.GetEnvironmentVariable(name) ?? "").Trim();
        return double.TryParse(v, out var n) ? n : defaultValue;
    }

    // ---------------- Evidence quality heuristics ----------------

    private static double ParseScoreFromSnippet(string? snippet)
    {
        if (string.IsNullOrWhiteSpace(snippet)) return 0;
        var m = Regex.Match(snippet, @"score\s*=\s*(?<s>[0-9]*\.?[0-9]+)", RegexOptions.IgnoreCase);
        if (!m.Success) return 0;
        return double.TryParse(m.Groups["s"].Value, out var v) ? v : 0;
    }

    private static double GetBestScore(IEnumerable<Citation> citations)
    {
        double best = 0;
        foreach (var c in citations)
        {
            var s = ParseScoreFromSnippet(c.Snippet);
            if (s > best) best = s;
        }
        return best;
    }

    private static bool IsLowOverlap(string question, string internalText, int minOverlap = 2)
    {
        if (string.IsNullOrWhiteSpace(question)) return true;
        if (string.IsNullOrWhiteSpace(internalText)) return true;

        var q = question.ToLowerInvariant();
        var t = internalText.ToLowerInvariant();

        var tokens = Regex.Matches(q, @"[a-z0-9\-]{4,}", RegexOptions.IgnoreCase)
            .Select(m => m.Value)
            .Distinct()
            .Take(25)
            .ToList();

        int hit = 0;
        foreach (var tok in tokens)
        {
            if (t.Contains(tok))
            {
                hit++;
                if (hit >= minOverlap) return false;
            }
        }
        return true;
    }

    private static bool IsTavilyConfigured()
    {
        var apiKey = Environment.GetEnvironmentVariable("TAVILY_API_KEY")?.Trim();
        return !string.IsNullOrWhiteSpace(apiKey) && !apiKey.Contains("<");
    }

    // ---------------- Web confirmation token (HMAC) ----------------
    // Env: WEBSEARCH_CONFIRM_SECRET
    // Token: base64url(payloadJson) + "." + base64url(hmacSha256(payloadJson))
    // payloadJson: {"exp":<unixSeconds>,"mh":"<sha256(message)>"}
    private static string MakeWebConfirmToken(string message, string requestId)
    {
        var secret = (Environment.GetEnvironmentVariable("WEBSEARCH_CONFIRM_SECRET") ?? "").Trim();
        if (string.IsNullOrWhiteSpace(secret))
            secret = "fallback-" + requestId;

        var exp = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds();
        var mh = Sha256Hex(message);

        var payloadJson = JsonSerializer.Serialize(new { exp, mh });
        var sig = HmacSha256(secret, payloadJson);

        return Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson)) + "." + Base64UrlEncode(sig);
    }

    private static bool ValidateWebConfirmToken(string token, string message)
    {
        var secret = (Environment.GetEnvironmentVariable("WEBSEARCH_CONFIRM_SECRET") ?? "").Trim();
        if (string.IsNullOrWhiteSpace(secret)) return false;

        var parts = token.Split('.', 2);
        if (parts.Length != 2) return false;

        byte[] payloadBytes;
        byte[] sigBytes;
        try
        {
            payloadBytes = Base64UrlDecode(parts[0]);
            sigBytes = Base64UrlDecode(parts[1]);
        }
        catch { return false; }

        var payloadJson = Encoding.UTF8.GetString(payloadBytes);

        var expectedSig = HmacSha256(secret, payloadJson);
        if (!CryptographicOperations.FixedTimeEquals(sigBytes, expectedSig))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;

            var exp = root.GetProperty("exp").GetInt64();
            var mh = root.GetProperty("mh").GetString() ?? "";

            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > exp) return false;
            return string.Equals(mh, Sha256Hex(message), StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static byte[] HmacSha256(string secret, string payload)
    {
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return h.ComputeHash(Encoding.UTF8.GetBytes(payload));
    }

    private static string Sha256Hex(string s)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    private static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        if (s.Length % 4 == 2) s += "==";
        else if (s.Length % 4 == 3) s += "=";
        return Convert.FromBase64String(s);
    }
}
