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
            // Log provider error body for debugging (do not leak to end-user)
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
