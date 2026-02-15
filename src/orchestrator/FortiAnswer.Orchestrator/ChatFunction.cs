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

            // 2) Validate core fields
            var message = body?.Message?.Trim();
            if (string.IsNullOrWhiteSpace(message))
                return await BadRequest(req, correlationId, "ValidationError", "Field 'message' is required.", new { field = "message" });

            var requestType = (body?.RequestType ?? Environment.GetEnvironmentVariable("DATA_BOUNDARY_DEFAULT") ?? "Public").Trim();
            var userRole = (body?.UserRole ?? "User").Trim();
            var userGroup = body?.UserGroup;
            var conversationId = body?.ConversationId;

            // 3) Read knobs
            var internalTopK = ReadTopK("INTERNAL_TOPK", 2, 1, 10);
            var webTopK = ReadTopK("WEB_TOPK", 3, 1, 10);

            var actionHints = new List<string>
            {
                $"cfg:internalTopK={internalTopK}",
                $"cfg:webTopK={webTopK}"
            };

            // 4) Internal retrieval
            var internalBundle = await _retrieval.RetrieveAsync(
                question: message!,
                requestType: requestType,
                userGroup: userGroup,
                topK: internalTopK,
                correlationId: correlationId
            );

            actionHints.Add(internalBundle.Citations.Count > 0 ? "used:internal_search" : "used:internal_search_empty");

            // 5) Decide if web search is needed (no/weak/irrelevant internal evidence)
            double minScore = GetDoubleEnv("SEARCH_MIN_SCORE", 0.01);
            double bestScore = GetBestScore(internalBundle.Citations);

            var internalTextForOverlap =
                (internalBundle.Context ?? "") + "\n\n" +
                string.Join("\n\n", internalBundle.Citations.Select(c => c.Snippet ?? ""));

            bool noEvidence = internalBundle.Citations.Count == 0;
            bool weakEvidence = !noEvidence && bestScore < minScore;
            bool lowOverlap = IsLowOverlap(message!, internalTextForOverlap, minOverlap: 2);

            actionHints.Add($"internal:bestScore={bestScore:0.###}");
            actionHints.Add($"internal:minScore={minScore:0.###}");
            actionHints.Add($"internal:lowOverlap={(lowOverlap ? "true" : "false")}");

            bool needWeb = noEvidence || weakEvidence || lowOverlap;

            // 6) Web search enabled only for Public + tavily configured
            bool isPublic = string.Equals(requestType, "Public", StringComparison.OrdinalIgnoreCase);
            var webMode = (Environment.GetEnvironmentVariable("WEBSEARCH_MODE") ?? "off").Trim().ToLowerInvariant();
            bool webEnabled = isPublic && webMode == "tavily" && IsTavilyConfigured();

            WebSearchBundle? webBundle = null;

            // 7) Two-step confirmation
            if (needWeb && webEnabled && body?.ConfirmWebSearch != true)
            {
                actionHints.Add("next:ask_user_for_web_search");

                var token = MakeWebConfirmToken(message!, correlationId);

                var resp = req.CreateResponse(HttpStatusCode.OK);
                resp.Headers.Add("Content-Type", "application/json");
                resp.Headers.Add("x-correlation-id", correlationId);

                var payloadAsk = new ChatResponse
                {
                    Answer =
                        "I searched the internal knowledge base but did not find sufficient relevant evidence.\n" +
                        "Do you want me to run a Web Search (Tavily) to supplement the answer?\n\n" +
                        "If yes, send the same request again with confirmWebSearch=true and include webSearchToken.",
                    Citations = internalBundle.Citations,
                    ActionHints = actionHints,
                    NeedsWebConfirmation = true,
                    WebSearchToken = token,
                    RequestId = correlationId,
                    Escalation = new EscalationInfo { ShouldEscalate = false, Reason = "" },
                    Mode = new ModeInfo
                    {
                        Retrieval = Environment.GetEnvironmentVariable("RETRIEVAL_MODE") ?? "stub",
                        Llm = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GROQ_API_KEY")) ? "stub" : "groq"
                    }
                };

                await resp.WriteStringAsync(JsonSerializer.Serialize(payloadAsk, JsonOut));
                return resp;
            }

            if (needWeb && webEnabled && body?.ConfirmWebSearch == true)
            {
                if (string.IsNullOrWhiteSpace(body.WebSearchToken))
                    return await BadRequest(req, correlationId, "ValidationError",
                        "Field 'webSearchToken' is required when confirmWebSearch=true.",
                        new { field = "webSearchToken" });

                if (!ValidateWebConfirmToken(body.WebSearchToken!, message!))
                    return await BadRequest(req, correlationId, "ValidationError",
                        "Invalid or expired webSearchToken.",
                        new { field = "webSearchToken" });

                webBundle = await _webSearch.SearchAsync(
                    query: message!,
                    topK: webTopK,
                    correlationId: correlationId
                );

                actionHints.Add("used:web_search");
            }
            else
            {
                actionHints.Add(needWeb ? "web_disabled_or_not_allowed" : "next:optional_web_search");
            }

            // 8) Build prompt and call LLM
            var prompt = _promptBuilder.Build(
                userMessage: message!,
                requestType: requestType,
                userRole: userRole,
                userGroup: userGroup,
                conversationId: conversationId,
                internalContext: internalBundle.Context,
                webContext: webBundle?.Context
            );

            var answer = await _groq.GenerateAsync(prompt, correlationId);

            // 9) Merge citations
            var merged = new List<Citation>();
            merged.AddRange(internalBundle.Citations);
            if (webBundle is not null) merged.AddRange(webBundle.Citations);

            // 10) Response
            var ok = req.CreateResponse(HttpStatusCode.OK);
            ok.Headers.Add("Content-Type", "application/json");
            ok.Headers.Add("x-correlation-id", correlationId);

            var payload = new ChatResponse
            {
                Answer = answer ?? "",
                Citations = merged,
                ActionHints = actionHints,
                NeedsWebConfirmation = false,
                WebSearchToken = null,
                RequestId = correlationId,
                Escalation = new EscalationInfo { ShouldEscalate = false, Reason = "" },
                Mode = new ModeInfo
                {
                    Retrieval = Environment.GetEnvironmentVariable("RETRIEVAL_MODE") ?? "stub",
                    Llm = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GROQ_API_KEY")) ? "stub" : "groq"
                }
            };

            await ok.WriteStringAsync(JsonSerializer.Serialize(payload, JsonOut));
            return ok;
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
            if (!string.IsNullOrWhiteSpace(v)) return v!.Trim();
        }
        return Guid.NewGuid().ToString("N");
    }

    private static int ReadTopK(string envName, int defaultValue, int min, int max)
    {
        var raw = (Environment.GetEnvironmentVariable(envName) ?? "").Trim();
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

    // Parses "[score=0.033 ...]" from snippet
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

    // Low overlap if fewer than minOverlap tokens (>=4 chars) from question appear in internalText
    private static bool IsLowOverlap(string question, string internalText, int minOverlap)
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
        var apiKey = (Environment.GetEnvironmentVariable("TAVILY_API_KEY") ?? "").Trim();
        return !string.IsNullOrWhiteSpace(apiKey) && !apiKey.Contains("<");
    }

    // --- Web confirmation token (HMAC) ---
    // Env: WEBSEARCH_CONFIRM_SECRET
    // Token: base64url(payloadJson) + "." + base64url(hmacSha256(payloadJson))
    // payloadJson: {"exp":<unixSeconds>,"mh":"<sha256(message)>"}
    private static string MakeWebConfirmToken(string message, string correlationId)
    {
        var secret = (Environment.GetEnvironmentVariable("WEBSEARCH_CONFIRM_SECRET") ?? "").Trim();
        if (string.IsNullOrWhiteSpace(secret))
        {
            // If not configured, refuse to generate a usable token (safer).
            // Return an obvious placeholder so caller knows it's misconfigured.
            return "missing-secret";
        }

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
        catch
        {
            return false;
        }

        var payloadJson = Encoding.UTF8.GetString(payloadBytes);

        // signature
        var expectedSig = HmacSha256(secret, payloadJson);
        if (!CryptographicOperations.FixedTimeEquals(sigBytes, expectedSig))
            return false;

        // payload
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("exp", out var expEl)) return false;
            if (!root.TryGetProperty("mh", out var mhEl)) return false;

            var exp = expEl.GetInt64();
            var mh = mhEl.GetString() ?? "";

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (now > exp) return false;

            var expectedMh = Sha256Hex(message);
            if (!string.Equals(mh, expectedMh, StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }
        catch
        {
            return false;
        }
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

    private static string Base64UrlEncode(byte[] data)
    {
        return Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
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
