namespace FortiAnswer.Orchestrator.Services;

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FortiAnswer.Orchestrator.Models;

public sealed class RetrievalBundle
{
    public string Context { get; set; } = "";
    public List<Citation> Citations { get; set; } = new();
}

public sealed class RetrievalService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IEmbeddingService _embeddings;

    public RetrievalService(IHttpClientFactory httpFactory, IEmbeddingService embeddings)
    {
        _httpFactory = httpFactory;
        _embeddings = embeddings;
    }

    public async Task<RetrievalBundle> RetrieveAsync(
        string question,
        string? requestType,
        string? userGroup,
        int topK,
        string correlationId)
    {
        var mode = (Environment.GetEnvironmentVariable("RETRIEVAL_MODE") ?? "stub")
            .Trim().ToLowerInvariant();

        if (mode != "azureaisearch")
            return Stub(question, requestType, userGroup, topK, correlationId);

        var endpoint = Environment.GetEnvironmentVariable("SEARCH_ENDPOINT")?.Trim();
        var indexName = Environment.GetEnvironmentVariable("SEARCH_INDEX")?.Trim();
        var apiKey = Environment.GetEnvironmentVariable("SEARCH_API_KEY")?.Trim();
        var apiVersion = Environment.GetEnvironmentVariable("SEARCH_API_VERSION")?.Trim() ?? "2024-07-01";

        if (string.IsNullOrWhiteSpace(endpoint) ||
            string.IsNullOrWhiteSpace(indexName) ||
            string.IsNullOrWhiteSpace(apiKey) ||
            endpoint.Contains("<") || indexName.Contains("<") || apiKey.Contains("<"))
        {
            Console.WriteLine("[search] missing/placeholder SEARCH_* config; using stub retrieval");
            return Stub(question, requestType, userGroup, topK, correlationId);
        }

        // keyword | vector | hybrid
        var queryMode = (Environment.GetEnvironmentVariable("RETRIEVAL_QUERY_MODE") ?? "hybrid")
            .Trim().ToLowerInvariant();

        // vector field
        var vectorField = (Environment.GetEnvironmentVariable("SEARCH_VECTOR_FIELD") ?? "contentVector").Trim();

        // ---- Field mapping (match your index) ----
        // Your index fields: id, content, source, path, chunkid, page, createdUtc, contentVector
        var fieldId = (Environment.GetEnvironmentVariable("SEARCH_FIELD_ID") ?? "id").Trim();
        var fieldContent = (Environment.GetEnvironmentVariable("SEARCH_FIELD_CONTENT") ?? "content").Trim();
        var fieldTitle = (Environment.GetEnvironmentVariable("SEARCH_FIELD_TITLE") ?? "path").Trim();   // use path as title
        var fieldUrl = (Environment.GetEnvironmentVariable("SEARCH_FIELD_URL") ?? "source").Trim();     // use source as url/id
        var fieldPath = (Environment.GetEnvironmentVariable("SEARCH_FIELD_PATH") ?? "path").Trim();
        var fieldChunkId = (Environment.GetEnvironmentVariable("SEARCH_FIELD_CHUNKID") ?? "chunkid").Trim();
        var fieldPage = (Environment.GetEnvironmentVariable("SEARCH_FIELD_PAGE") ?? "page").Trim();
        var fieldCreatedUtc = (Environment.GetEnvironmentVariable("SEARCH_FIELD_CREATEDUTC") ?? "createdUtc").Trim();

        // ---- Select (IMPORTANT: do NOT include title/url because they don't exist in your index) ----
        var select = Environment.GetEnvironmentVariable("SEARCH_SELECT")?.Trim();
        if (string.IsNullOrWhiteSpace(select))
        {
            select = string.Join(",",
                fieldId,
                fieldContent,
                "source",
                fieldPath,
                fieldChunkId,
                fieldPage,
                fieldCreatedUtc
            );
        }

        // Optional filter template (only if you add such fields to the index later)
        // Example: "requestType eq '{requestType}' and userGroup eq '{userGroup}'"
        var filterTemplate = Environment.GetEnvironmentVariable("SEARCH_FILTER_TEMPLATE")?.Trim();
        string? filter = null;
        if (!string.IsNullOrWhiteSpace(filterTemplate))
        {
            filter = filterTemplate
                .Replace("{requestType}", EscapeODataString(requestType ?? ""))
                .Replace("{userGroup}", EscapeODataString(userGroup ?? ""));
            if (string.IsNullOrWhiteSpace(filter)) filter = null;
        }

        // ---- score threshold (optional) ----
        // If set, drops hits with @search.score < minScore
        var minScoreRaw = (Environment.GetEnvironmentVariable("SEARCH_MIN_SCORE") ?? "").Trim();
        double? minScore = null;
        if (double.TryParse(minScoreRaw, out var ms) && ms >= 0)
            minScore = ms;

        var client = _httpFactory.CreateClient();
        var url = $"{endpoint.TrimEnd('/')}/indexes/{indexName}/docs/search?api-version={apiVersion}";

        // 1) embed if vector/hybrid
        float[]? qVec = null;
        if (queryMode is "vector" or "hybrid")
        {
            try
            {
                qVec = await _embeddings.EmbedAsync(question);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[search.embed.error] {ex.GetType().Name}: {ex.Message}. Falling back to keyword mode.");
                queryMode = "keyword";
            }
        }

        // 2) payload
        object payload = queryMode switch
        {
            "vector" => new
            {
                search = "*",
                top = topK,
                vectorQueries = new[]
                {
                    new {
                        kind = "vector",
                        vector = qVec,
                        fields = vectorField,
                        k = topK
                    }
                },
                select,
                filter
            },

            "hybrid" => new
            {
                search = question,
                top = topK,
                queryType = "simple",
                vectorQueries = new[]
                {
                    new {
                        kind = "vector",
                        vector = qVec,
                        fields = vectorField,
                        k = topK
                    }
                },
                select,
                filter
            },

            _ => new
            {
                search = question,
                top = topK,
                queryType = "simple",
                select,
                filter
            }
        };

        Console.WriteLine(
            $"[search] queryMode={queryMode} endpoint={endpoint} index={indexName} topK={topK} " +
            $"vectorField={vectorField} select={select} filter={(filter ?? "(none)")} minScore={(minScore?.ToString("0.###") ?? "(none)")}"
        );

        var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Add("x-correlation-id", correlationId);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.Add("api-key", apiKey);
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var resp = await client.SendAsync(req);
        var json = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
        {
            Console.WriteLine($"[search.error] status={(int)resp.StatusCode} body={json}");
            return Stub(question, requestType, userGroup, topK, correlationId);
        }

        using var doc = JsonDocument.Parse(json);

        var citations = new List<Citation>();
        if (doc.RootElement.TryGetProperty("value", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in arr.EnumerateArray())
            {
                var score = GetNumber(item, "@search.score");

                // ---- score filter ----
                if (minScore.HasValue)
                {
                    var s = score ?? 0;
                    if (s < minScore.Value)
                        continue;
                }

                var title = GetAnyAsString(item, fieldTitle) ?? "Document";
                var urlOrId =
                    GetAnyAsString(item, fieldUrl) ??
                    GetAnyAsString(item, fieldId) ??
                    "search://doc";

                var snippet = GetAnyAsString(item, fieldContent) ?? "";

                // Debug metadata
                var path = GetAnyAsString(item, fieldPath);
                var chunkId = GetAnyAsString(item, fieldChunkId);
                var page = GetAnyAsString(item, fieldPage);
                var createdUtc = GetAnyAsString(item, fieldCreatedUtc);

                var meta =
                    $"[score={(score?.ToString("0.###") ?? "")} path={path ?? ""} chunkid={chunkId ?? ""} page={page ?? ""} createdUtc={createdUtc ?? ""}]\n";

                citations.Add(new Citation
                {
                    Title = title,
                    UrlOrId = urlOrId,
                    Snippet = Truncate(meta + snippet, 1400)
                });

                if (citations.Count >= topK) break;
            }
        }

        var header =
            $"[internal-evidence]\n" +
            $"requestType={requestType ?? ""}\n" +
            $"userGroup={userGroup ?? ""}\n" +
            $"topK={topK}\n" +
            $"source=azureaisearch\n" +
            $"retrievalQueryMode={queryMode}\n" +
            $"vectorField={vectorField}\n" +
            $"minScore={(minScore?.ToString("0.###") ?? "")}\n";

        var body = citations.Count == 0
            ? "[no internal evidence found]"
            : string.Join("\n\n", citations.Select((c, i) =>
                $"[Chunk {i + 1}] {c.Title}\nSource: {c.UrlOrId}\n{c.Snippet}"
            ));

        return new RetrievalBundle
        {
            Context = header + "\n" + body,
            Citations = citations
        };
    }

    private static RetrievalBundle Stub(string question, string? requestType, string? userGroup, int topK, string correlationId)
    {
        var citations = new List<Citation>
        {
            new Citation
            {
                Title = "Internal KB (stub)",
                UrlOrId = "kb://stub/doc1#chunk1",
                Snippet = Truncate(
                    "Placeholder evidence. Replace with real retrieved text. When you integrate Azure AI Search chunks, this stub will be replaced.",
                    500
                )
            }
        };

        var context =
            $"[internal-evidence stub]\n" +
            $"topK={topK}\n" +
            $"requestType={requestType ?? ""}\n" +
            $"userGroup={userGroup ?? ""}\n" +
            $"question={question}\n\n" +
            string.Join("\n\n", citations.Select((c, i) => $"[Chunk {i + 1}] {c.Title}\n{c.Snippet}"));

        return new RetrievalBundle { Context = context, Citations = citations };
    }

    // supports string / number / bool
    private static string? GetAnyAsString(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object) return null;
        if (!obj.TryGetProperty(name, out var p)) return null;

        return p.ValueKind switch
        {
            JsonValueKind.String => p.GetString(),
            JsonValueKind.Number => p.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static double? GetNumber(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object) return null;
        if (!obj.TryGetProperty(name, out var p)) return null;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out var d)) return d;
        return null;
    }

    private static string EscapeODataString(string s) => (s ?? "").Replace("'", "''");

    private static string Truncate(string? s, int maxChars)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= maxChars ? s : s.Substring(0, maxChars) + "…";
    }
}
