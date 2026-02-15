namespace FortiAnswer.Orchestrator.Services;

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FortiAnswer.Orchestrator.Models;

public sealed class WebSearchBundle
{
    public string Context { get; set; } = "";
    public List<Citation> Citations { get; set; } = new();
}

public sealed class WebSearchService
{
    private readonly IHttpClientFactory _httpFactory;

    public WebSearchService(IHttpClientFactory httpFactory)
    {
        _httpFactory = httpFactory;
    }

    public async Task<WebSearchBundle> SearchAsync(string query, int topK, string correlationId)
    {
        var mode = (Environment.GetEnvironmentVariable("WEBSEARCH_MODE") ?? "off")
            .Trim().ToLowerInvariant();

        if (mode != "tavily")
            return new WebSearchBundle { Context = "[web-search disabled]", Citations = new() };

        var apiKey = Environment.GetEnvironmentVariable("TAVILY_API_KEY")?.Trim();
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Contains("<"))
            return new WebSearchBundle { Context = "[web-search not configured]", Citations = new() };

        var client = _httpFactory.CreateClient();
        client.BaseAddress = new Uri("https://api.tavily.com/");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var payload = new
        {
            api_key = apiKey,
            query = query,
            max_results = Math.Max(1, Math.Min(topK, 5)),
            search_depth = "basic",
            include_answer = false,
            include_raw_content = false
        };

        var req = new HttpRequestMessage(HttpMethod.Post, "search");
        req.Headers.Add("x-correlation-id", correlationId);
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var resp = await client.SendAsync(req);
        var json = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
        {
            Console.WriteLine($"[websearch.error] status={(int)resp.StatusCode} body={json}");
            if (resp.StatusCode == HttpStatusCode.TooManyRequests)
                return new WebSearchBundle { Context = "[web-search rate-limited]", Citations = new() };

            return new WebSearchBundle { Context = "[web-search failed]", Citations = new() };
        }

        using var doc = JsonDocument.Parse(json);
        var citations = new List<Citation>();

        if (doc.RootElement.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in results.EnumerateArray())
            {
                var title = r.TryGetProperty("title", out var t) ? t.GetString() : "Web result";
                var url = r.TryGetProperty("url", out var u) ? u.GetString() : "";
                var content = r.TryGetProperty("content", out var c) ? c.GetString() : "";

                citations.Add(new Citation
                {
                    Title = title,
                    UrlOrId = url,
                    Snippet = Truncate(content, 800)
                });

                if (citations.Count >= topK) break;
            }
        }

        var header = "[web-evidence]\n";
        var body = citations.Count == 0
            ? "[no web evidence found]"
            : string.Join("\n\n", citations.Select((c, i) =>
                $"[Web {i + 1}] {c.Title}\nSource: {c.UrlOrId}\n{c.Snippet}"
              ));

        return new WebSearchBundle
        {
            Context = header + body,
            Citations = citations
        };
    }

    private static string Truncate(string? s, int maxChars)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= maxChars ? s : s.Substring(0, maxChars) + "…";
    }
}
