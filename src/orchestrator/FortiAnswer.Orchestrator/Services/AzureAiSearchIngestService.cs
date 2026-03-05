using System.Net;
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

    private const string KEY_FIELD = "id";
    private const string PATH_FIELD = "path";

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

    /// <summary>
    /// Delete ALL documents (chunks) whose path equals the given blob path.
    /// Example path: public/FAQ-PUBLIC-VPN-TROUBLESHOOTING-20260217.docx
    /// </summary>
    public async Task<int> DeleteByPathAsync(string path, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path))
            return 0;

        // 1) query ids by filter
        var ids = await GetDocIdsByPathAsync(path.Trim(), ct);

        // 2) batch delete by ids
        return await DeleteDocsByIdsAsync(ids, ct);
    }

    private async Task<List<string>> GetDocIdsByPathAsync(string path, CancellationToken ct)
    {
        // OData escaping: ' -> ''
        var safe = path.Replace("'", "''");
        var filter = $"{PATH_FIELD} eq '{safe}'";

        var url = $"{_endpoint}/indexes/{_index}/docs/search?api-version={_apiVersion}";
        var requestBody = new
        {
            search = "*",
            filter,
            top = 1000,
            select = KEY_FIELD
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Add("api-key", _adminKey);
        req.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

        using var res = await _http.SendAsync(req, ct);
        var text = await res.Content.ReadAsStringAsync(ct);

        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"Search query failed: {(int)res.StatusCode} {text}");

        var ids = new List<string>();

        using var doc = JsonDocument.Parse(text);
        if (doc.RootElement.TryGetProperty("value", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in arr.EnumerateArray())
            {
                if (item.TryGetProperty(KEY_FIELD, out var idEl))
                {
                    var id = idEl.GetString();
                    if (!string.IsNullOrWhiteSpace(id)) ids.Add(id);
                }
            }
        }

        return ids;
    }

    private async Task<int> DeleteDocsByIdsAsync(IEnumerable<string> ids, CancellationToken ct)
    {
        var list = ids.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        if (list.Count == 0) return 0;

        // Azure Search index batch supports up to 1000 actions typically.
        // For safety, delete in chunks.
        const int batchSize = 500;
        int deleted = 0;

        for (int i = 0; i < list.Count; i += batchSize)
        {
            var batch = list.Skip(i).Take(batchSize).ToList();

            var actions = batch.Select(id => new Dictionary<string, object>
            {
                ["@search.action"] = "delete",
                [KEY_FIELD] = id
            });

            await UploadAsync(actions, ct);
            deleted += batch.Count;
        }

        return deleted;
    }
}