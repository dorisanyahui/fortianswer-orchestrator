using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;


namespace FortiAnswer.Orchestrator.Services;

public sealed class AzureAiSearchIngestService
{
    private readonly HttpClient _http;
    private readonly string _endpoint;
    private readonly string _index;
    private readonly string _apiVersion;
    private readonly string _adminKey;

    public AzureAiSearchIngestService(HttpClient http, IConfiguration cfg)
    {
        _http = http;
        _endpoint = cfg["SEARCH_ENDPOINT"] ?? throw new InvalidOperationException("SEARCH_ENDPOINT missing");
        _index = cfg["SEARCH_INDEX"] ?? throw new InvalidOperationException("SEARCH_INDEX missing");
        _apiVersion = cfg["SEARCH_API_VERSION"] ?? "2024-07-01";
        _adminKey = cfg["SEARCH_ADMIN_KEY"] ?? throw new InvalidOperationException("SEARCH_ADMIN_KEY missing");
    }

    public async Task UploadAsync(IEnumerable<object> actions, CancellationToken ct)
    {
        var url = $"{_endpoint}/indexes/{_index}/docs/index?api-version={_apiVersion}";
        var payload = new Dictionary<string, object> { ["value"] = actions };

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Add("api-key", _adminKey);
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var res = await _http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Search upload failed: {(int)res.StatusCode} {body}");
        }
    }
}
