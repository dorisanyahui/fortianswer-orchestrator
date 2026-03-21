using System.Text.Json;
using Azure.Data.Tables;

namespace FortiAnswer.Orchestrator.Services;

public sealed class FeedbackTableService
{
    private readonly TableClient _table;

    public FeedbackTableService(string storageConnectionString)
    {
        var tableName = Environment.GetEnvironmentVariable("FEEDBACK_TABLE_NAME") ?? "Feedback";
        _table = new TableClient(storageConnectionString, tableName);
        _table.CreateIfNotExists();
    }

    // ── Write ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Records a rating. Overwrites if the user changes their mind.
    /// "down" ratings are automatically flagged for admin review.
    /// </summary>
    public async Task RecordAsync(
        string requestId,
        string username,
        string rating,           // "up" | "down"
        string? issueType  = null,
        string[]? citations = null)
    {
        var entity = new TableEntity("feedback", requestId)
        {
            ["RequestId"]   = requestId,
            ["Username"]    = username,
            ["Rating"]      = rating,
            ["IssueType"]   = issueType ?? "",
            ["Citations"]   = citations is { Length: > 0 }
                                  ? JsonSerializer.Serialize(citations)
                                  : "",
            ["NeedsReview"] = rating == "down",   // auto-flag low ratings
            ["ReviewedAt"]  = "",
            ["CreatedUtc"]  = DateTimeOffset.UtcNow.ToString("o")
        };

        await _table.UpsertEntityAsync(entity, TableUpdateMode.Replace);
    }

    // ── Admin: dismiss a flagged review ──────────────────────────────────────

    /// <summary>
    /// Marks a flagged response as reviewed by an admin. Returns false if not found.
    /// </summary>
    public async Task<bool> DismissAsync(string requestId)
    {
        var res = await _table.GetEntityIfExistsAsync<TableEntity>("feedback", requestId);
        if (!res.HasValue || res.Value is null) return false;

        var e = res.Value;
        e["NeedsReview"] = false;
        e["ReviewedAt"]  = DateTimeOffset.UtcNow.ToString("o");

        await _table.UpdateEntityAsync(e, e.ETag, TableUpdateMode.Replace);
        return true;
    }

    // ── Query ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all responses flagged for review that have not yet been dismissed.
    /// </summary>
    public async Task<List<FlaggedFeedback>> GetFlaggedAsync()
    {
        var results = new List<FlaggedFeedback>();

        await foreach (var e in _table.QueryAsync<TableEntity>("PartitionKey eq 'feedback'"))
        {
            var needsReview = e.TryGetValue("NeedsReview", out var nr) && nr is bool b && b;
            var reviewedAt  = GetStr(e, "ReviewedAt");

            if (!needsReview || !string.IsNullOrWhiteSpace(reviewedAt)) continue;

            results.Add(new FlaggedFeedback
            {
                RequestId  = GetStr(e, "RequestId")  ?? e.RowKey,
                Username   = GetStr(e, "Username")   ?? "",
                IssueType  = GetStr(e, "IssueType")  ?? "",
                Citations  = ParseCitations(GetStr(e, "Citations")),
                CreatedUtc = GetStr(e, "CreatedUtc") ?? ""
            });
        }

        results.Sort((a, b) => string.Compare(b.CreatedUtc, a.CreatedUtc, StringComparison.Ordinal));
        return results;
    }

    /// <summary>
    /// Aggregates all feedback into a summary: overall, per issueType, per citation document.
    /// </summary>
    public async Task<FeedbackSummary> GetSummaryAsync()
    {
        var totalUp   = 0;
        var totalDown = 0;
        var byIssue   = new Dictionary<string, (int up, int down)>(StringComparer.OrdinalIgnoreCase);
        var byCite    = new Dictionary<string, (int up, int down)>(StringComparer.OrdinalIgnoreCase);

        await foreach (var e in _table.QueryAsync<TableEntity>("PartitionKey eq 'feedback'"))
        {
            var rating    = GetStr(e, "Rating") ?? "";
            var issueType = GetStr(e, "IssueType") ?? "General";
            var citations = ParseCitations(GetStr(e, "Citations"));
            var isUp      = string.Equals(rating, "up", StringComparison.OrdinalIgnoreCase);

            if (isUp) totalUp++; else totalDown++;

            // per issueType
            byIssue.TryGetValue(issueType, out var ic);
            byIssue[issueType] = isUp ? (ic.up + 1, ic.down) : (ic.up, ic.down + 1);

            // per citation document
            foreach (var cite in citations)
            {
                byCite.TryGetValue(cite, out var cc);
                byCite[cite] = isUp ? (cc.up + 1, cc.down) : (cc.up, cc.down + 1);
            }
        }

        var total = totalUp + totalDown;

        return new FeedbackSummary
        {
            TotalUp            = totalUp,
            TotalDown          = totalDown,
            TotalRatings       = total,
            SatisfactionRate   = total == 0 ? 0 : Math.Round(totalUp * 100.0 / total, 1),
            ByIssueType        = byIssue
                .Select(kv => new IssueTypeStat
                {
                    IssueType        = kv.Key,
                    Up               = kv.Value.up,
                    Down             = kv.Value.down,
                    SatisfactionRate = kv.Value.up + kv.Value.down == 0
                                        ? 0
                                        : Math.Round(kv.Value.up * 100.0 / (kv.Value.up + kv.Value.down), 1)
                })
                .OrderBy(s => s.SatisfactionRate)   // worst first — helps admin prioritise
                .ToList(),
            ByCitation         = byCite
                .Select(kv => new CitationStat
                {
                    DocumentId       = kv.Key,
                    Up               = kv.Value.up,
                    Down             = kv.Value.down,
                    TotalRatings     = kv.Value.up + kv.Value.down,
                    SatisfactionRate = kv.Value.up + kv.Value.down == 0
                                        ? 0
                                        : Math.Round(kv.Value.up * 100.0 / (kv.Value.up + kv.Value.down), 1)
                })
                .OrderBy(s => s.SatisfactionRate)   // worst first
                .ToList()
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string[] ParseCitations(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<string[]>(json) ?? []; }
        catch { return []; }
    }

    private static string? GetStr(TableEntity e, string name)
        => e.TryGetValue(name, out var v) ? v?.ToString() : null;
}

// ── Response models ───────────────────────────────────────────────────────────

public sealed class FeedbackSummary
{
    public int    TotalUp          { get; set; }
    public int    TotalDown        { get; set; }
    public int    TotalRatings     { get; set; }
    public double SatisfactionRate { get; set; }   // percentage, e.g. 84.0
    public List<IssueTypeStat> ByIssueType { get; set; } = [];
    public List<CitationStat>  ByCitation  { get; set; } = [];
}

public sealed class IssueTypeStat
{
    public string IssueType        { get; set; } = "";
    public int    Up               { get; set; }
    public int    Down             { get; set; }
    public double SatisfactionRate { get; set; }
}

public sealed class CitationStat
{
    public string DocumentId       { get; set; } = "";
    /// <summary>Filename extracted from DocumentId, e.g. "vpn-setup-guide.docx"</summary>
    public string FileName         => Path.GetFileName(DocumentId);
    public int    Up               { get; set; }
    public int    Down             { get; set; }
    public int    TotalRatings     { get; set; }
    public double SatisfactionRate { get; set; }
}

public sealed class FlaggedFeedback
{
    public string   RequestId  { get; set; } = "";
    public string   Username   { get; set; } = "";
    public string   IssueType  { get; set; } = "";
    public string[] Citations  { get; set; } = [];
    public string   CreatedUtc { get; set; } = "";
}
