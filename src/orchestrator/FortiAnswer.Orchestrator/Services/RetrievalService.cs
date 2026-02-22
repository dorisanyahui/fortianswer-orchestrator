using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace FortiAnswer.Orchestrator.Services;

/// <summary>
/// Azure AI Search retrieval service (supports keyword/vector/hybrid).
/// Adds mandatory DataBoundary (Public/Internal/Confidential/Restricted) filter using "classification" field.
/// </summary>
public sealed class RetrievalService
{
    private readonly IHttpClientFactory _httpFactory;

    public RetrievalService(IHttpClientFactory httpFactory)
    {
        _httpFactory = httpFactory;
    }

    // Small helper: read env with default.
    private static string Env(string key, string def)
        => (Environment.GetEnvironmentVariable(key) ?? def).Trim();

    private static int EnvInt(string key, int def)
        => int.TryParse(Environment.GetEnvironmentVariable(key), out var v) ? v : def;

    private static double EnvDouble(string key, double def)
        => double.TryParse(Environment.GetEnvironmentVariable(key), out var v) ? v : def;

    private static string NormalizeBoundary(string? boundary)
    {
        var b = (boundary ?? "").Trim();
        if (string.IsNullOrWhiteSpace(b)) return "Public";
        return b switch
        {
            "public" => "Public",
            "internal" => "Internal",
            "confidential" => "Confidential",
            "restricted" => "Restricted",
            _ => char.ToUpperInvariant(b[0]) + b[1..]
        };
    }

    /// <summary>
    /// Build an OData filter based on DataBoundary and optional extra filter template.
    /// You must have a filterable index field (default: "classification") with values:
    /// public/internal/confidential/restricted
    /// </summary>
    private static string BuildBoundaryFilter(string boundary, string fieldClassification)
    {
        // NOTE: use lowercase stored values in index
        return boundary switch
        {
            "Internal" => $"{fieldClassification} eq 'public' or {fieldClassification} eq 'internal'",
            "Confidential" => $"{fieldClassification} eq 'public' or {fieldClassification} eq 'internal' or {fieldClassification} eq 'confidential'",
            "Restricted" => $"{fieldClassification} eq 'restricted'",
            _ => $"{fieldClassification} eq 'public'",
        };
    }

    /// <summary>
    /// Retrieve context + citations. Caller passes requestType as DataBoundary for now.
    /// </summary>
    public async Task<Dictionary<string, object>> RetrieveAsync(
        string query,
        string? requestType,
        string? userRole,
        string? userGroup,
        CancellationToken ct = default)
    {
        // --------- ENV ---------
        var mode = Env("RETRIEVAL_MODE", "azureaisearch"); // keep compatibility
        if (!string.Equals(mode, "azureaisearch", StringComparison.OrdinalIgnoreCase))
        {
            return new Dictionary<string, object>
            {
                ["Mode"] = mode,
                ["Context"] = "",
                ["Citations"] = Array.Empty<object>(),
                ["Debug"] = "RETRIEVAL_MODE is not azureaisearch."
            };
        }

        var endpoint = Env("SEARCH_ENDPOINT", "");
        var index = Env("SEARCH_INDEX", "");
        var apiKey = Env("SEARCH_API_KEY", "");
        var apiVersion = Env("SEARCH_API_VERSION", "2024-07-01");

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(index) || string.IsNullOrWhiteSpace(apiKey))
        {
            return new Dictionary<string, object>
            {
                ["Mode"] = mode,
                ["Context"] = "",
                ["Citations"] = Array.Empty<object>(),
                ["Debug"] = "Missing SEARCH_ENDPOINT / SEARCH_INDEX / SEARCH_API_KEY."
            };
        }

        var topK = EnvInt("INTERNAL_TOPK", 3);
        var select = Env("SEARCH_SELECT", "id,content,source,path,chunkid,page,createdUtc");
        var queryMode = Env("RETRIEVAL_QUERY_MODE", "hybrid"); // keyword|vector|hybrid
        var vectorField = Env("SEARCH_VECTOR_FIELD", "contentVector");
        var minScore = EnvDouble("SEARCH_MIN_SCORE", 0.0);

        // --------- Boundary filter ---------
        // We treat requestType as DataBoundary (Public/Internal/Confidential/Restricted)
        var boundary = NormalizeBoundary(requestType ?? Env("SEARCH_DEFAULT_BOUNDARY", "Public"));
        var fieldClassification = Env("SEARCH_FIELD_CLASSIFICATION", "classification");
        var boundaryFilter = BuildBoundaryFilter(boundary, fieldClassification);

        // Optional: allow adding extra filter template via env (kept for compatibility)
        // Example: "({boundaryFilter}) and (tenant eq '{userGroup}')"
        var filterTemplate = (Environment.GetEnvironmentVariable("SEARCH_FILTER_TEMPLATE") ?? "").Trim();
        string finalFilter = boundaryFilter;

        if (!string.IsNullOrWhiteSpace(filterTemplate))
        {
            // very simple token replace, but keep safe defaults
            finalFilter = filterTemplate
                .Replace("{boundaryFilter}", boundaryFilter, StringComparison.OrdinalIgnoreCase)
                .Replace("{requestType}", boundary, StringComparison.OrdinalIgnoreCase)
                .Replace("{userRole}", (userRole ?? "").Replace("'", "''"), StringComparison.OrdinalIgnoreCase)
                .Replace("{userGroup}", (userGroup ?? "").Replace("'", "''"), StringComparison.OrdinalIgnoreCase);
        }

        // --------- Build search request ---------
        var url = $"{endpoint.TrimEnd('/')}/indexes/{index}/docs/search?api-version={apiVersion}";
        var http = _httpFactory.CreateClient();

        http.DefaultRequestHeaders.Clear();
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        http.DefaultRequestHeaders.Add("api-key", apiKey);

        // payload differs slightly by query mode
        object payload = queryMode.ToLowerInvariant() switch
        {
            "keyword" => new
            {
                search = query,
                top = topK,
                select,
                filter = finalFilter
            },

            "vector" => new
            {
                search = "",
                top = topK,
                select,
                filter = finalFilter,
                vectorQueries = new object[]
                {
                    new { kind = "text", text = query, k = topK, fields = vectorField }
                }
            },

            _ => new // hybrid default
            {
                search = query,
                top = topK,
                select,
                filter = finalFilter,
                vectorQueries = new object[]
                {
                    new { kind = "text", text = query, k = topK, fields = vectorField }
                }
            }
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        using var resp = await http.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"), ct);

        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            return new Dictionary<string, object>
            {
                ["Mode"] = mode,
                ["Boundary"] = boundary,
                ["Filter"] = finalFilter,
                ["Context"] = "",
                ["Citations"] = Array.Empty<object>(),
                ["Debug"] = $"Search failed: {(int)resp.StatusCode} {resp.ReasonPhrase}. Body: {body}"
            };
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        // Azure Search response usually has "value": [ ... ]
        if (!root.TryGetProperty("value", out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return new Dictionary<string, object>
            {
                ["Mode"] = mode,
                ["Boundary"] = boundary,
                ["Filter"] = finalFilter,
                ["Context"] = "",
                ["Citations"] = Array.Empty<object>(),
                ["Debug"] = "No 'value' array in response."
            };
        }

        var citations = new List<Dictionary<string, object>>();
        var sb = new StringBuilder();

        foreach (var item in arr.EnumerateArray())
        {
            // Optional score check
            double score = 0;
            if (item.TryGetProperty("@search.score", out var scoreEl) && scoreEl.ValueKind == JsonValueKind.Number)
                score = scoreEl.GetDouble();

            if (score < minScore) continue;

            string content = item.TryGetProperty("content", out var c) ? (c.GetString() ?? "") : "";
            string source = item.TryGetProperty("source", out var s) ? (s.GetString() ?? "") : "";
            string path = item.TryGetProperty("path", out var p) ? (p.GetString() ?? "") : "";
            string id = item.TryGetProperty("id", out var idEl) ? (idEl.GetString() ?? "") : "";

            // Build context
            if (!string.IsNullOrWhiteSpace(content))
            {
                sb.AppendLine(content.Trim());
                sb.AppendLine();
            }

            // Build citations
            var cite = new Dictionary<string, object>
            {
                ["id"] = id,
                ["source"] = source,
                ["path"] = path,
                ["score"] = score
            };

            if (item.TryGetProperty("chunkid", out var ch) && ch.ValueKind == JsonValueKind.Number)
             cite["chunkId"] = ch.GetInt32();
            if (item.TryGetProperty("page", out var pg))
            {
                if (pg.ValueKind == JsonValueKind.Number) cite["page"] = pg.GetInt32();
                else cite["page"] = pg.GetString() ?? "";
            }
            if (item.TryGetProperty("createdUtc", out var cu)) cite["createdUtc"] = cu.GetString() ?? "";

            citations.Add(cite);
        }

        return new Dictionary<string, object>
        {
            ["Mode"] = mode,
            ["Boundary"] = boundary,
            ["Filter"] = finalFilter,
            ["Context"] = sb.ToString().Trim(),
            ["Citations"] = citations
        };
    }
}