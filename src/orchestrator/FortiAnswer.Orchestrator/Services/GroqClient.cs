namespace FortiAnswer.Orchestrator.Services;

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

public sealed class GroqClient
{
    private readonly IHttpClientFactory _httpFactory;

    public GroqClient(IHttpClientFactory httpFactory)
    {
        _httpFactory = httpFactory;
    }

    // Wrapper to match ChatFunction.cs call-site (single-turn, no history)
    public Task<string> ChatAsync(string prompt, string correlationId)
        => GenerateAsync(prompt, correlationId);

    /// <summary>
    /// Multi-turn chat: sends a system prompt, previous conversation turns, and the current user message.
    /// This gives the LLM memory of the current session.
    /// </summary>
    public async Task<string> ChatWithHistoryAsync(
        string systemPrompt,
        IEnumerable<TableStorageService.ConversationTurn> history,
        string userMessage,
        string correlationId)
    {
        var apiKey = Environment.GetEnvironmentVariable("GROQ_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
            return "[error] GROQ_API_KEY not configured.";

        var model = Environment.GetEnvironmentVariable("GROQ_MODEL") ?? "llama-3.3-70b-versatile";

        // Build messages array: system → alternating user/assistant pairs → current user message
        var messages = new List<object>
        {
            new { role = "system", content = systemPrompt }
        };

        foreach (var turn in history)
        {
            messages.Add(new { role = "user",      content = turn.UserMessage });
            messages.Add(new { role = "assistant", content = turn.BotAnswer   });
        }

        messages.Add(new { role = "user", content = userMessage });

        var client = _httpFactory.CreateClient();
        client.BaseAddress = new Uri("https://api.groq.com/openai/v1/");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        var payload = new { model, messages, temperature = 0.2, max_tokens = 500 };

        var httpReq = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
        httpReq.Headers.Add("x-correlation-id", correlationId);
        httpReq.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            System.Text.Encoding.UTF8,
            "application/json");

        var resp = await client.SendAsync(httpReq);
        var json = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
        {
            Console.WriteLine($"[groq.error] status={(int)resp.StatusCode} body={json}");
            if (resp.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                return "[error] Groq rate limit hit (429). Try again later.";
            return $"[error] Groq call failed ({(int)resp.StatusCode}).";
        }

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "";
    }

    public async Task<string> GenerateAsync(string prompt, string correlationId)
    {
        var apiKey = Environment.GetEnvironmentVariable("GROQ_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
            return "[error] GROQ_API_KEY not configured.";

        var model = Environment.GetEnvironmentVariable("GROQ_MODEL") ?? "llama-3.3-70b-versatile";
        Console.WriteLine($"[groq.model] using model={model}");

        var client = _httpFactory.CreateClient();
        client.BaseAddress = new Uri("https://api.groq.com/openai/v1/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var payload = new
        {
            model = model,
            messages = new[]
            {
                new { role = "user", content = prompt }
            },
            temperature = 0.2,
            max_tokens = 500
        };

        var req = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
        req.Headers.Add("x-correlation-id", correlationId);
        req.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json"
        );

        var resp = await client.SendAsync(req);
        var json = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
        {
            Console.WriteLine($"[groq.error] status={(int)resp.StatusCode} body={json}");

            if (resp.StatusCode == (HttpStatusCode)429)
                return "[error] Groq rate limit hit (429). Try again later or use fallback.";

            return $"[error] Groq call failed ({(int)resp.StatusCode}).";
        }

        using var doc = JsonDocument.Parse(json);
        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        return content ?? "";
    }
}
