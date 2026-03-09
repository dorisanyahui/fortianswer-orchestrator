using System.IO;
using System.Linq;
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
    private readonly TableStorageService _tables;
    private readonly TicketsTableService _tickets;

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
        GroqClient groq,
        TableStorageService tables,
        TicketsTableService tickets)
    {
        _log = loggerFactory.CreateLogger<ChatFunction>();
        _retrieval = retrieval;
        _webSearch = webSearch;
        _promptBuilder = promptBuilder;
        _groq = groq;
        _tables = tables;
        _tickets = tickets;
    }

    [Function("chat")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "chat")] HttpRequestData req)
    {
        var swTotal = System.Diagnostics.Stopwatch.StartNew();

        var clientCorrelationId = GetClientCorrelationId(req);
        var requestId = Guid.NewGuid().ToString("N");

        // We'll try to log at the end no matter what.
        string? conversationId = null;
        string? username = null;
        string? userRole = null;
        string? requestedBoundary = null;
        string? allowedBoundary = null;
        string? boundary = null;
        string? issueType = null;
        string? userGroup = null;

        string outcome = "unknown";
        string usedRetrieval = "none";
        double? bestScore = null;
        double? minScore = null;
        bool noEvidence = false;
        bool weakEvidence = false;
        bool lowOverlap = false;
        bool needWeb = false;

        string? retrievalBoundary = null;
        string? retrievalFilter = null;
        string? retrievalDebug = null;

        bool isPublic = false;
        string webMode = "off";
        bool webEnabled = false;
        bool webUsed = false;

        string? webToken = null;
        int internalCitationsRawCount = 0;
        int internalCitationsFilteredCount = 0;
        int webCitationsCount = 0;

        string? errorCode = null;
        string? errorMessage = null;

        try
        {
            // 1) Read body
            var bodyText = await new StreamReader(req.Body).ReadToEndAsync();
            if (string.IsNullOrWhiteSpace(bodyText))
            {
                outcome = "bad_request";
                errorCode = "MissingBody";
                errorMessage = "Request body is required.";
                return await BadRequest(req, requestId, clientCorrelationId, errorCode, errorMessage);
            }

            ChatRequest? body;
            try
            {
                body = JsonSerializer.Deserialize<ChatRequest>(bodyText, JsonIn);
            }
            catch
            {
                outcome = "bad_request";
                errorCode = "InvalidJson";
                errorMessage = "Invalid JSON body.";
                return await BadRequest(req, requestId, clientCorrelationId, errorCode, errorMessage);
            }

            // 2) Validate
            var message = body?.Message?.Trim();
            if (string.IsNullOrWhiteSpace(message))
            {
                outcome = "bad_request";
                errorCode = "ValidationError";
                errorMessage = "Field 'message' is required.";
                return await BadRequest(req, requestId, clientCorrelationId, errorCode, errorMessage, new { field = "message" });
            }

            // ✅ ConversationId auto-generate (NEW)
            conversationId = body?.ConversationId;
            if (string.IsNullOrWhiteSpace(conversationId))
            {
                conversationId = "conv-" + requestId[..8];
            }

            username = body?.Username?.Trim().ToLowerInvariant() ?? "anonymous";

            // 3) Resolve role + boundary (server-side)
            var headerRole = GetHeader(req, "x-user-role");
            userRole = NormalizeRole(headerRole ?? body?.UserRole ?? "Customer");

            issueType = NormalizeIssueType(body?.IssueType);

            requestedBoundary = NormalizeBoundary(
                body?.DataBoundary
                ?? body?.RequestType
                ?? Environment.GetEnvironmentVariable("DATA_BOUNDARY_DEFAULT")
                ?? Environment.GetEnvironmentVariable("SEARCH_DEFAULT_BOUNDARY")
                ?? "Public"
            );

            allowedBoundary = DecideBoundary(userRole, requestedBoundary);

            boundary = string.Equals(requestedBoundary, "Restricted", StringComparison.OrdinalIgnoreCase)
                ? "Restricted"
                : allowedBoundary;

            // Server-side override: certain issueTypes always force Restricted
            // regardless of what boundary the client sent or what the role allows.
            // This ensures the web UI never needs to expose a "Restricted" option.
            if (IsAlwaysRestricted(issueType))
                boundary = "Restricted";

            userGroup = body?.UserGroup;

            // TopK config for web only
            var webTopK = ReadTopK("WEB_TOPK", 3, 1, 10);

            // 4) Policy gates
            if (string.Equals(boundary, "Restricted", StringComparison.OrdinalIgnoreCase))
            {
                outcome = "escalated";
                usedRetrieval = "none";

                // Admin: just explain, no ticket.
                // Customer / Agent: auto-create ticket.
                string? autoTicketId = null;
                if (!string.Equals(userRole, "Admin", StringComparison.OrdinalIgnoreCase))
                {
                    autoTicketId = await SafeCreateTicketAsync(
                        convId:           conversationId,
                        createdByUser:    body?.Username?.Trim().ToLowerInvariant() ?? "anonymous",
                        issueType:        issueType ?? "General",
                        dataBoundary:     "Restricted",
                        summary:          $"Auto-escalated: Restricted content request. Message: {message![..Math.Min(200, message.Length)]}",
                        escalationReason: "Restricted content requires escalation.",
                        source:           "auto");
                }

                var answer = string.Equals(userRole, "Admin", StringComparison.OrdinalIgnoreCase)
                    ? "This request involves Restricted content, which cannot be served through the chat interface — even for Admins.\n\n" +
                      "Restricted playbooks and procedures must be accessed through the secure out-of-band channel (e.g., your IR runbook vault or incident bridge).\n\n" +
                      "If this is an active incident, initiate the incident response process directly and coordinate with your security team."
                    : "This request falls under Restricted content. I can’t provide step-by-step playbook details in chat.\n\n" +
                      "A ticket has been created and will be assigned to an authorized responder.\n\n" +
                      "To help them act quickly, please provide: who is affected, when it started, and whether MFA was approved or credentials were entered.";

                var response = new
                {
                    requestId,
                    conversationId,
                    answer,
                    citations = new List<Citation>(),
                    needsWebConfirmation = false,
                    webSearchToken = (string?)null,
                    next = autoTicketId is not null
                        ? (object)new { action = "escalate", ticketId = autoTicketId }
                        : new { action = "none" },
                    escalation = new { shouldEscalate = true, reason = "Restricted content requires escalation." },
                    mode = new
                    {
                        retrieval = Environment.GetEnvironmentVariable("RETRIEVAL_MODE") ?? "stub",
                        llm = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GROQ_API_KEY")) ? "stub" : "groq"
                    }
                };

                await SafeWriteLogAsync(requestId, conversationId, outcome, ticketId: autoTicketId);
                return await Ok(req, requestId, clientCorrelationId, response);
            }

            if (string.Equals(requestedBoundary, "Confidential", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(userRole, "Admin", StringComparison.OrdinalIgnoreCase))
            {
                outcome = "escalated";
                usedRetrieval = "none";

                // Customer / Agent requesting Confidential → auto-create ticket.
                var autoTicketId = await SafeCreateTicketAsync(
                    convId:           conversationId,
                    createdByUser:    body?.Username?.Trim().ToLowerInvariant() ?? "anonymous",
                    issueType:        issueType ?? "General",
                    dataBoundary:     "Confidential",
                    summary:          $"Auto-escalated: Confidential request by non-Admin ({userRole}). Message: {message![..Math.Min(200, message.Length)]}",
                    escalationReason: "Confidential request requires Admin access.",
                    source:           "auto");

                var answer = """
This request explicitly requires Confidential handling, but you are not authorized (Admin) to access Confidential content.

I’ll escalate this request to an authorized responder.
""";

                var response = new
                {
                    requestId,
                    conversationId,
                    answer,
                    citations = new List<Citation>(),
                    needsWebConfirmation = false,
                    webSearchToken = (string?)null,
                    next = (object)new { action = "escalate", ticketId = autoTicketId },
                    escalation = new { shouldEscalate = true, reason = "Confidential request requires Admin access." },
                    mode = new
                    {
                        retrieval = Environment.GetEnvironmentVariable("RETRIEVAL_MODE") ?? "stub",
                        llm = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GROQ_API_KEY")) ? "stub" : "groq"
                    }
                };

                await SafeWriteLogAsync(requestId, conversationId, outcome, ticketId: autoTicketId);
                return await Ok(req, requestId, clientCorrelationId, response);
            }

            // 5) Internal retrieval
            var internalBundle = await _retrieval.RetrieveAsync(
                query: message!,
                requestType: boundary,
                userRole: userRole,
                userGroup: userGroup,
                ct: default
            );

            retrievalBoundary = ExtractString(internalBundle, "Boundary");
            retrievalFilter = ExtractString(internalBundle, "Filter");
            retrievalDebug = ExtractString(internalBundle, "Debug");

            var internalContext = ExtractString(internalBundle, "Context") ?? "";
            var internalCitationsRaw = ExtractCitations(internalBundle, "Citations");
            internalCitationsRawCount = internalCitationsRaw.Count;

            usedRetrieval = "internal_search";

            // 6) Evidence heuristics
            minScore = GetDoubleEnv("SEARCH_MIN_SCORE", 0.01);
            bestScore = GetBestScore(internalCitationsRaw);

            var internalTextForOverlap =
                internalContext + "\n\n" +
                string.Join("\n\n", internalCitationsRaw.ConvertAll(c => c.Snippet ?? ""));

            noEvidence = internalCitationsRaw.Count == 0;
            weakEvidence = !noEvidence && bestScore < minScore;
            lowOverlap = IsLowOverlap(message!, internalTextForOverlap, minOverlap: 2);

            needWeb = noEvidence || weakEvidence || lowOverlap;

            // Filter off-topic internal citations
            var internalCitationsFiltered = (lowOverlap || weakEvidence)
                ? new List<Citation>()
                : internalCitationsRaw;

            internalCitationsFilteredCount = internalCitationsFiltered.Count;

            // 7) Web enablement (ONLY Public)
            isPublic = string.Equals(boundary, "Public", StringComparison.OrdinalIgnoreCase);
            webMode = (Environment.GetEnvironmentVariable("WEBSEARCH_MODE") ?? "off").Trim().ToLowerInvariant();
            webEnabled = isPublic && webMode == "tavily" && IsTavilyConfigured();

            object? webBundle = null;
            string? webContext = null;

            var mergedCitations = new List<Citation>(internalCitationsFiltered);

            // Step 1: need web but not confirmed => ask user
            if (needWeb && webEnabled && body?.ConfirmWebSearch != true)
            {
                outcome = "needs_web_confirmation";

                webToken = MakeWebConfirmToken(message!, requestId);

                var answer =
                    "Internal knowledge base did not return enough relevant evidence to answer this question.\n" +
                    "Would you like me to run a Web Search (Tavily) to supplement the answer?\n\n" +
                    "If yes, resend the request with confirmWebSearch=true and include webSearchToken.";

                var response = new
                {
                    requestId,
                    conversationId,
                    answer,
                    citations = mergedCitations,
                    needsWebConfirmation = true,
                    webSearchToken = webToken,
                    next = new { action = "none" }, // ✅ NEW
                    escalation = new { shouldEscalate = false, reason = "" },
                    mode = new
                    {
                        retrieval = Environment.GetEnvironmentVariable("RETRIEVAL_MODE") ?? "stub",
                        llm = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GROQ_API_KEY")) ? "stub" : "groq"
                    }
                };

                await SafeWriteLogAsync(requestId, conversationId, outcome);
                return await Ok(req, requestId, clientCorrelationId, response);
            }

            // Step 2: confirmed => validate token => run web search
            if (needWeb && webEnabled && body?.ConfirmWebSearch == true)
            {
                if (string.IsNullOrWhiteSpace(body.WebSearchToken))
                {
                    outcome = "bad_request";
                    errorCode = "ValidationError";
                    errorMessage = "Field 'webSearchToken' is required when confirmWebSearch=true.";
                    return await BadRequest(req, requestId, clientCorrelationId, errorCode, errorMessage, new { field = "webSearchToken" });
                }

                if (!ValidateWebConfirmToken(body.WebSearchToken!, message!))
                {
                    outcome = "bad_request";
                    errorCode = "ValidationError";
                    errorMessage = "Invalid or expired webSearchToken.";
                    return await BadRequest(req, requestId, clientCorrelationId, errorCode, errorMessage, new { field = "webSearchToken" });
                }

                webBundle = await _webSearch.SearchAsync(
                    query: message!,
                    topK: webTopK,
                    correlationId: requestId
                );

                webContext = ExtractString(webBundle, "Context");
                var webCitations = ExtractCitations(webBundle, "Citations");
                webCitationsCount = webCitations.Count;

                if (webCitations.Count > 0)
                    mergedCitations.AddRange(webCitations);

                usedRetrieval = "web_search";
                webUsed = true;
            }

            // 8) Build prompt
            var basePrompt = _promptBuilder.Build(
                userMessage: message!,
                requestType: boundary,
                userRole: userRole,
                userGroup: userGroup,
                conversationId: conversationId,
                internalContext: internalContext,
                webContext: webContext
            );

            var escalationHint =
                "\n\n=== ESCALATION STATE ===\n" +
                "Escalation: None (this chat response is not escalated unless the API returns escalation.shouldEscalate=true)\n" +
                "=== END ESCALATION STATE ===\n";

            var issueHint =
                "\n\n=== ISSUE TYPE (CONTEXT) ===\n" +
                $"IssueType: {issueType}\n" +
                "Guidance:\n" +
                "- VPN/MFA/PasswordReset: end-user friendly, step-by-step, safety reminders.\n" +
                "- Phishing/AccountLockout/Severity/SuspiciousLogin/EndpointAlert: internal workflow, ticket notes, escalation criteria.\n" +
                "=== END ISSUE TYPE ===\n";

            var guardrails =
                "\n\n=== RESPONSE RULES (IMPORTANT) ===\n" +
                "1) Use only facts supported by the provided citations (especially dates, versions, affected products, and mitigations).\n" +
                "2) If a detail is not supported by citations, say \"Not confirmed by sources\" and do not guess.\n" +
                "3) Do NOT paste raw URLs in the answer. If a source is needed, rely on the citations list.\n" +
                "4) Escalation language control: This chat response is NOT automatically escalated. Do not say or imply \"I escalated\" / \"after escalation\" / \"we have escalated\".\n" +
                "   You MAY mention escalation only as a conditional next step (e.g., \"If confirmed, escalate to Tier-2\") when explicitly supported by citations.\n" +
                "5) Prefer concise, actionable mitigations.\n" +
                "=== END RULES ===\n";

            var prompt = basePrompt + escalationHint + issueHint + guardrails;

            // 9) Groq generate
            var answerText = await _groq.GenerateAsync(prompt, requestId) ?? "";

            // 10) Response (NO DEBUG returned)
            outcome = "answered";

            var responseFinal = new
            {
                requestId,
                conversationId,
                answer = answerText,
                citations = mergedCitations,
                needsWebConfirmation = false,
                webSearchToken = (string?)null,
                next = new { action = "none" }, // ✅ NEW
                escalation = new { shouldEscalate = false, reason = "" },
                mode = new
                {
                    retrieval = Environment.GetEnvironmentVariable("RETRIEVAL_MODE") ?? "stub",
                    llm = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GROQ_API_KEY")) ? "stub" : "groq"
                }
            };

            await SafeWriteLogAsync(requestId, conversationId, outcome);
            return await Ok(req, requestId, clientCorrelationId, responseFinal);
        }
        catch (Exception ex)
        {
            outcome = "error";
            errorCode = "InternalError";
            errorMessage = "Unexpected error.";

            _log.LogError(ex, "ChatFunction failed. requestId={requestId} clientCorrelationId={clientCorrelationId}", requestId, clientCorrelationId);

            await SafeWriteLogAsync(requestId, conversationId, outcome, ex);

            return await InternalError(req, requestId, clientCorrelationId, errorCode, errorMessage);
        }
        finally
        {
            swTotal.Stop();
        }

        async Task<string?> SafeCreateTicketAsync(
            string? convId,
            string createdByUser,
            string issueType,
            string dataBoundary,
            string summary,
            string escalationReason,
            string source)
        {
            try
            {
                return await _tickets.CreateAsync(
                    conversationId:   convId,
                    createdByUser:    createdByUser,
                    issueType:        issueType,
                    dataBoundary:     dataBoundary,
                    summary:          summary,
                    escalationReason: escalationReason,
                    source:           source);
            }
            catch (Exception ticketEx)
            {
                _log.LogWarning(ticketEx, "Failed to auto-create ticket. requestId={requestId}", requestId);
                return null;
            }
        }

        async Task SafeWriteLogAsync(string reqId, string? convId, string outc, Exception? ex = null, string? ticketId = null)
        {
            try
            {
                swTotal.Stop();
                var latencyMs = swTotal.ElapsedMilliseconds;

                var debugObj = new
                {
                    serverRequestId = reqId,
                    clientCorrelationId,
                    role = userRole,
                    requestedBoundary,
                    allowedBoundary,
                    boundary,
                    issueType,
                    userGroup,
                    conversationId = convId,
                    outcome = outc,
                    retrieval = new
                    {
                        used = usedRetrieval,
                        internalBundle = new
                        {
                            boundary = retrievalBoundary,
                            filter = retrievalFilter,
                            debug = retrievalDebug
                        },
                        scores = new { bestScore, minScore },
                        heuristics = new { noEvidence, weakEvidence, lowOverlap, needWeb },
                        citations = new
                        {
                            internalRaw = internalCitationsRawCount,
                            internalFiltered = internalCitationsFilteredCount,
                            web = webCitationsCount
                        }
                    },
                    web = new
                    {
                        isPublic,
                        webMode,
                        webEnabled,
                        webUsed,
                        webTokenIssued = !string.IsNullOrWhiteSpace(webToken)
                    },
                    timingMs = new { total = latencyMs },
                    error = ex is null ? null : new
                    {
                        type = ex.GetType().FullName,
                        message = ex.Message,
                        stack = ex.StackTrace
                    }
                };

                await _tables.WriteConversationLogAsync(
                    requestId: reqId,
                    conversationId: convId ?? "",
                    username: username ?? "anonymous",
                    outcome: outc,
                    role: userRole ?? "Unknown",
                    dataBoundary: boundary ?? "Unknown",
                    requestType: issueType ?? "General",
                    usedRetrieval: usedRetrieval,
                    topScore: bestScore,
                    ticketId: ticketId,
                    latencyMs: latencyMs,
                    debugObj: debugObj
                );
            }
            catch (Exception logEx)
            {
                _log.LogWarning(logEx, "Failed to write ConversationLog. requestId={requestId}", requestId);
            }
        }
    }

    // ---------------- Role / Boundary / Issue helpers ----------------

    private static string NormalizeRole(string role)
    {
        role = (role ?? "").Trim();
        if (string.IsNullOrWhiteSpace(role)) return "Customer";
        role = role.ToLowerInvariant();
        return role switch
        {
            "customer" => "Customer",
            "user" => "Customer",
            "enduser" => "Customer",
            "agent" => "Agent",
            "soc" => "Agent",
            "analyst" => "Agent",
            "admin" => "Admin",
            "it" => "Admin",
            _ => char.ToUpperInvariant(role[0]) + role[1..]
        };
    }

    /// <summary>
    /// IssueTypes that always force Restricted boundary regardless of user role or
    /// requested boundary. The web UI never needs to expose a "Restricted" option —
    /// the server decides based on what the user is asking about.
    /// SuspiciousLogin / Severity = active threat indicators requiring human escalation.
    /// </summary>
    private static bool IsAlwaysRestricted(string? issueType) =>
        issueType is "SuspiciousLogin" or "Severity";

    private static string NormalizeBoundary(string boundary)
    {
        boundary = (boundary ?? "").Trim();
        if (string.IsNullOrWhiteSpace(boundary)) return "Public";
        boundary = boundary.ToLowerInvariant();
        return boundary switch
        {
            "public" => "Public",
            "internal" => "Internal",
            "confidential" => "Confidential",
            "restricted" => "Restricted",
            _ => char.ToUpperInvariant(boundary[0]) + boundary[1..]
        };
    }

    private static string NormalizeIssueType(string? t)
    {
        t = (t ?? "").Trim();
        if (string.IsNullOrWhiteSpace(t)) return "General";
        t = t.ToLowerInvariant();

        return t switch
        {
            "vpn" => "VPN",
            "mfa" => "MFA",
            "passwordreset" => "PasswordReset",
            "password_reset" => "PasswordReset",
            "phishing" => "Phishing",
            "suspiciouslogin" => "SuspiciousLogin",
            "endpointalert" => "EndpointAlert",
            "accountlockout" => "AccountLockout",
            "lockout" => "AccountLockout",
            "severity" => "Severity",
            _ => "General"
        };
    }

    private static int BoundaryRank(string boundary) => NormalizeBoundary(boundary) switch
    {
        "Public" => 0,
        "Internal" => 1,
        "Confidential" => 2,
        "Restricted" => 3,
        _ => 0
    };

    private static string DecideBoundary(string userRole, string requestedBoundary)
    {
        int req = BoundaryRank(requestedBoundary);
        int max = userRole switch
        {
            "Admin" => BoundaryRank("Confidential"),
            "Agent" => BoundaryRank("Internal"),
            _ => BoundaryRank("Public"),
        };

        int use = Math.Min(req, max);

        return use switch
        {
            0 => "Public",
            1 => "Internal",
            2 => "Confidential",
            3 => "Restricted",
            _ => "Public"
        };
    }

    private static string? GetHeader(HttpRequestData req, string name)
    {
        if (req.Headers.TryGetValues(name, out var vals))
        {
            var v = vals?.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(v)) return v;
        }
        return null;
    }

    // ---------------- Reflection / dictionary helpers ----------------

    private static string? ExtractString(object? obj, string propertyName)
    {
        if (obj is null) return null;

        try
        {
            if (obj is IDictionary<string, object> dict)
            {
                if (dict.TryGetValue(propertyName, out var v) && v is not null)
                    return v.ToString();
                return null;
            }

            if (obj is JsonElement je)
            {
                if (je.ValueKind == JsonValueKind.Object && je.TryGetProperty(propertyName, out var prop))
                    return prop.ToString();
                return null;
            }

            var propInfo = obj.GetType().GetProperty(propertyName);
            return propInfo?.GetValue(obj)?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static List<Citation> ExtractCitations(object? obj, string propertyName)
    {
        var list = new List<Citation>();
        if (obj is null) return list;

        try
        {
            object? raw = null;

            if (obj is IDictionary<string, object> dict)
            {
                dict.TryGetValue(propertyName, out raw);
            }
            else
            {
                var prop = obj.GetType().GetProperty(propertyName);
                raw = prop?.GetValue(obj);
            }

            if (raw is null) return list;

            if (raw is IEnumerable<Citation> typed)
                return new List<Citation>(typed);

            if (raw is JsonElement je && je.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in je.EnumerateArray())
                {
                    var title = item.TryGetProperty("title", out var t) ? t.GetString() : null;
                    var urlOrId = item.TryGetProperty("urlOrId", out var u) ? u.GetString()
                               : item.TryGetProperty("url", out var u2) ? u2.GetString()
                               : item.TryGetProperty("id", out var i2) ? i2.GetString() : null;
                    var snippet = item.TryGetProperty("snippet", out var s) ? s.GetString() : null;

                    list.Add(new Citation { Title = title, UrlOrId = urlOrId, Snippet = snippet });
                }
                return list;
            }

            if (raw is System.Collections.IEnumerable any)
            {
                foreach (var item in any)
                {
                    if (item is null) continue;

                    if (item is IDictionary<string, object> d)
                    {
                        var path = d.TryGetValue("path", out var pv) ? pv?.ToString() : null;
                        var source = d.TryGetValue("source", out var sv) ? sv?.ToString() : null;
                        var id = d.TryGetValue("id", out var iv) ? iv?.ToString() : null;

                        var scoreStr = "";
                        if (d.TryGetValue("score", out var sc) && sc is not null)
                            scoreStr = sc.ToString() ?? "";

                        var snippet = string.IsNullOrWhiteSpace(scoreStr) ? null : $"score={scoreStr}";

                        list.Add(new Citation
                        {
                            Title = path ?? source ?? id,
                            UrlOrId = path ?? source ?? id,
                            Snippet = snippet
                        });

                        continue;
                    }

                    var t = item.GetType();

                    string? title = t.GetProperty("Title")?.GetValue(item)?.ToString()
                                 ?? t.GetProperty("title")?.GetValue(item)?.ToString()
                                 ?? t.GetProperty("path")?.GetValue(item)?.ToString();

                    string? urlOrId =
                        t.GetProperty("UrlOrId")?.GetValue(item)?.ToString()
                        ?? t.GetProperty("urlOrId")?.GetValue(item)?.ToString()
                        ?? t.GetProperty("Url")?.GetValue(item)?.ToString()
                        ?? t.GetProperty("url")?.GetValue(item)?.ToString()
                        ?? t.GetProperty("Id")?.GetValue(item)?.ToString()
                        ?? t.GetProperty("id")?.GetValue(item)?.ToString();

                    string? snippetText = t.GetProperty("Snippet")?.GetValue(item)?.ToString()
                                       ?? t.GetProperty("snippet")?.GetValue(item)?.ToString();

                    list.Add(new Citation { Title = title, UrlOrId = urlOrId, Snippet = snippetText });
                }
            }
        }
        catch
        {
            // ignore
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

    // ---------------- Evidence heuristics ----------------

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