using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace FortiAnswer.Orchestrator
{
    public sealed class ChatFunction
    {
        private readonly ILogger _log;
        private readonly IHttpClientFactory _httpClientFactory;

        // Keep these as object so DI wiring doesn't break even if types/methods changed.
        // We intentionally do NOT call missing methods like PromptBuilder.BuildPrompt or GroqClient.ChatAsync.
        private readonly object? _groqClient;
        private readonly object? _retrievalService;
        private readonly object? _promptBuilder;

        private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        public ChatFunction(
            ILoggerFactory loggerFactory,
            IHttpClientFactory httpClientFactory,
            object? groqClient = null,
            object? retrievalService = null,
            object? promptBuilder = null)
        {
            _log = loggerFactory.CreateLogger<ChatFunction>();
            _httpClientFactory = httpClientFactory;

            _groqClient = groqClient;
            _retrievalService = retrievalService;
            _promptBuilder = promptBuilder;
        }

        [Function("chat")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "chat")] HttpRequestData req,
            CancellationToken ct)
        {
            try
            {
                var body = await ReadBodyAsync(req, ct);
                var chatReq = JsonSerializer.Deserialize<ChatRequest>(body, JsonOpts);

                if (chatReq is null || string.IsNullOrWhiteSpace(chatReq.Message))
                {
                    return await WriteJsonAsync(req, HttpStatusCode.BadRequest, new ErrorResponse
                    {
                        Error = "Invalid request. Expecting JSON with at least: { \"message\": \"...\" }"
                    }, ct);
                }

                // Optional internal retrieval: we keep it non-breaking (no compile-time dependency).
                // If your RetrievalService has a known method you want to call, you can wire it back later.
                var sources = new List<SourceItem>();
                var contextText = string.Empty;

                // Build prompt/messages (simple + robust)
                var system = string.IsNullOrWhiteSpace(chatReq.SystemPrompt)
                    ? "You are a helpful assistant. Answer clearly and cite sources if provided."
                    : chatReq.SystemPrompt.Trim();

                var userMessage = chatReq.Message.Trim();

                if (!string.IsNullOrWhiteSpace(contextText))
                {
                    userMessage =
                        "Use the following context to answer. If context is insufficient, say so.\n\n" +
                        "CONTEXT:\n" + contextText + "\n\n" +
                        "QUESTION:\n" + userMessage;
                }

                // ✅ FIX: Do NOT call _groqClient.ChatAsync (missing). Use Groq OpenAI-compatible REST.
                var answer = await CallGroqChatCompletionsAsync(
                    systemPrompt: system,
                    userPrompt: userMessage,
                    temperature: chatReq.Temperature ?? 0.2,
                    maxTokens: chatReq.MaxTokens ?? 600,
                    ct: ct);

                var resp = new ChatResponse
                {
                    Answer = answer ?? string.Empty,
                    Sources = sources
                };

                return await WriteJsonAsync(req, HttpStatusCode.OK, resp, ct);
            }
            catch (JsonException jex)
            {
                _log.LogWarning(jex, "Invalid JSON.");
                return await WriteJsonAsync(req, HttpStatusCode.BadRequest, new ErrorResponse
                {
                    Error = "Invalid JSON format."
                }, ct);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "ChatFunction failed.");
                return await WriteJsonAsync(req, HttpStatusCode.InternalServerError, new ErrorResponse
                {
                    Error = "Internal server error.",
                    Detail = ex.Message
                }, ct);
            }
        }

        private async Task<string?> CallGroqChatCompletionsAsync(
            string systemPrompt,
            string userPrompt,
            double temperature,
            int maxTokens,
            CancellationToken ct)
        {
            var apiKey = Environment.GetEnvironmentVariable("GROQ_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("Missing environment variable: GROQ_API_KEY");

            var model = Environment.GetEnvironmentVariable("GROQ_MODEL");
            if (string.IsNullOrWhiteSpace(model))
                model = "llama-3.3-70b-versatile";

            // Groq OpenAI-compatible endpoint
            var url = Environment.GetEnvironmentVariable("GROQ_BASE_URL");
            if (string.IsNullOrWhiteSpace(url))
                url = "https://api.groq.com/openai/v1/chat/completions";

            var payload = new GroqChatCompletionsRequest
            {
                Model = model,
                Temperature = temperature,
                MaxTokens = maxTokens,
                Messages = new List<GroqMessage>
                {
                    new() { Role = "system", Content = systemPrompt },
                    new() { Role = "user", Content = userPrompt }
                }
            };

            var json = JsonSerializer.Serialize(payload, JsonOpts);

            var client = _httpClientFactory.CreateClient("groq");
            using var httpReq = new HttpRequestMessage(HttpMethod.Post, url);
            httpReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            httpReq.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var httpResp = await client.SendAsync(httpReq, ct);
            var respText = await httpResp.Content.ReadAsStringAsync(ct);

            if (!httpResp.IsSuccessStatusCode)
            {
                _log.LogWarning("Groq error {StatusCode}: {Body}", (int)httpResp.StatusCode, respText);
                throw new InvalidOperationException($"Groq call failed: {(int)httpResp.StatusCode} {httpResp.ReasonPhrase}");
            }

            var parsed = JsonSerializer.Deserialize<GroqChatCompletionsResponse>(respText, JsonOpts);
            return parsed?.Choices is { Count: > 0 }
                ? parsed.Choices[0].Message?.Content
                : string.Empty;
        }

        private static async Task<string> ReadBodyAsync(HttpRequestData req, CancellationToken ct)
        {
            using var reader = new StreamReader(req.Body);
            return await reader.ReadToEndAsync(ct);
        }

        private static async Task<HttpResponseData> WriteJsonAsync<T>(
            HttpRequestData req,
            HttpStatusCode status,
            T payload,
            CancellationToken ct)
        {
            var resp = req.CreateResponse(status);
            resp.Headers.Add("Content-Type", "application/json; charset=utf-8");
            var json = JsonSerializer.Serialize(payload, JsonOpts);
            await resp.WriteStringAsync(json, ct);
            return resp;
        }

        // ======== Request/Response Contracts ========

        public sealed class ChatRequest
        {
            [JsonPropertyName("message")]
            public string? Message { get; set; }

            // Optional knobs
            [JsonPropertyName("systemPrompt")]
            public string? SystemPrompt { get; set; }

            [JsonPropertyName("temperature")]
            public double? Temperature { get; set; }

            [JsonPropertyName("maxTokens")]
            public int? MaxTokens { get; set; }
        }

        public sealed class ChatResponse
        {
            [JsonPropertyName("answer")]
            public string Answer { get; set; } = string.Empty;

            [JsonPropertyName("sources")]
            public List<SourceItem> Sources { get; set; } = new();
        }

        public sealed class SourceItem
        {
            [JsonPropertyName("title")]
            public string? Title { get; set; }

            [JsonPropertyName("url")]
            public string? Url { get; set; }

            [JsonPropertyName("snippet")]
            public string? Snippet { get; set; }
        }

        public sealed class ErrorResponse
        {
            [JsonPropertyName("error")]
            public string Error { get; set; } = "Error";

            [JsonPropertyName("detail")]
            public string? Detail { get; set; }
        }

        // ======== Groq OpenAI-Compatible DTOs ========

        private sealed class GroqChatCompletionsRequest
        {
            [JsonPropertyName("model")]
            public string Model { get; set; } = "llama-3.3-70b-versatile";

            [JsonPropertyName("messages")]
            public List<GroqMessage> Messages { get; set; } = new();

            [JsonPropertyName("temperature")]
            public double Temperature { get; set; } = 0.2;

            [JsonPropertyName("max_tokens")]
            public int MaxTokens { get; set; } = 600;
        }

        private sealed class GroqMessage
        {
            [JsonPropertyName("role")]
            public string Role { get; set; } = "user";

            [JsonPropertyName("content")]
            public string Content { get; set; } = string.Empty;
        }

        private sealed class GroqChatCompletionsResponse
        {
            [JsonPropertyName("choices")]
            public List<GroqChoice> Choices { get; set; } = new();
        }

        private sealed class GroqChoice
        {
            [JsonPropertyName("message")]
            public GroqChoiceMessage? Message { get; set; }
        }

        private sealed class GroqChoiceMessage
        {
            [JsonPropertyName("content")]
            public string? Content { get; set; }
        }
    }
}
