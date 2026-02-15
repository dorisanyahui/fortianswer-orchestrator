using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using System.Linq;

namespace FortiAnswer.Orchestrator.Services;

public sealed class OpenAiEmbeddingService : IEmbeddingService
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _model;

    private const int ExpectedDims = 1536;

    public OpenAiEmbeddingService(HttpClient http, IConfiguration cfg)
    {
        _http = http;
        _apiKey = cfg["OPENAI_API_KEY"] ?? throw new InvalidOperationException("OPENAI_API_KEY missing");
        _model = cfg["OPENAI_EMBED_MODEL"] ?? "text-embedding-3-small";
    }

    public async Task<float[]> EmbedAsync(string input, CancellationToken ct = default)
    {
        var vectors = await EmbedBatchAsync(new List<string> { input }, ct);
        return vectors[0];
    }

    public async Task<List<float[]>> EmbedBatchAsync(List<string> inputs, CancellationToken ct = default)
    {
        if (inputs is null || inputs.Count == 0) return new List<float[]>();

        for (int attempt = 0; attempt < 6; attempt++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/embeddings");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

            var payload = new { model = _model, input = inputs };
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var res = await _http.SendAsync(req, ct);

            if (res.IsSuccessStatusCode)
            {
                using var stream = await res.Content.ReadAsStreamAsync(ct);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

                var data = doc.RootElement.GetProperty("data");

                var ordered = data.EnumerateArray()
                    .Select(el => new
                    {
                        Index = el.GetProperty("index").GetInt32(),
                        Vec = el.GetProperty("embedding").EnumerateArray()
                            .Select(x => (float)x.GetDouble())
                            .ToArray()
                    })
                    .OrderBy(x => x.Index)
                    .ToList();

                if (ordered.Count != inputs.Count)
                    throw new InvalidOperationException($"Embeddings count mismatch: got {ordered.Count}, expected {inputs.Count}");

                foreach (var item in ordered)
                    if (item.Vec.Length != ExpectedDims)
                        throw new InvalidOperationException($"Unexpected embedding dims: {item.Vec.Length}");

                return ordered.Select(x => x.Vec).ToList();
            }

            var code = (int)res.StatusCode;
            var body = await res.Content.ReadAsStringAsync(ct);

            if (code == 429 || code >= 500)
            {
                var delaySeconds = Math.Min(30, Math.Pow(2, attempt));
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ct);
                continue;
            }

            throw new InvalidOperationException($"OpenAI embeddings failed: {code} {body}");
        }

        throw new InvalidOperationException("OpenAI embeddings failed after retries.");
    }
}
