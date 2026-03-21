using System.Text.Json;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;

namespace FortiAnswer.Orchestrator.Services;

public sealed class TableStorageService
{
    private readonly TableServiceClient _svc;
    private readonly ILogger<TableStorageService> _log;

    public TableStorageService(string storageConnectionString, ILogger<TableStorageService> log)
    {
        _svc = new TableServiceClient(storageConnectionString);
        _log = log;
    }

    private TableClient LogsTable() => _svc.GetTableClient("ConversationLogs");

    public async Task WriteConversationLogAsync(
        string requestId,
        string conversationId,
        string username,
        string outcome,
        string role,
        string dataBoundary,
        string requestType,
        string usedRetrieval,
        double? topScore,
        string? ticketId,
        long? latencyMs,
        object? debugObj,
        string? userMessage  = null,
        string? botAnswer    = null)
    {
        var table = LogsTable();
        await table.CreateIfNotExistsAsync();

        var pk = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var rk = $"{DateTime.UtcNow:HHmmssfff}-{Guid.NewGuid():N}";

        var entity = new TableEntity(pk, rk)
        {
            ["CreatedAtUtc"]   = DateTime.UtcNow.ToString("o"),
            ["RequestId"]      = requestId,
            ["ConversationId"] = conversationId,
            ["Username"]       = username,
            ["Outcome"]        = outcome,
            ["Role"]           = role,
            ["DataBoundary"]   = dataBoundary,
            ["RequestType"]    = requestType,
            ["UsedRetrieval"]  = usedRetrieval,
            ["TopScore"]       = topScore is null ? null : topScore.Value.ToString("0.####"),
            ["TicketId"]       = ticketId,
            ["LatencyMs"]      = latencyMs is null ? null : latencyMs.Value.ToString(),
            ["DebugJson"]      = debugObj is null ? null : JsonSerializer.Serialize(debugObj),
            ["UserMessage"]    = userMessage ?? "",
            ["BotAnswer"]      = botAnswer   ?? ""
        };

        await table.AddEntityAsync(entity);

        _log.LogInformation("ConversationLog written. requestId={RequestId} PK={PK} RK={RK}", requestId, pk, rk);
    }

    /// <summary>
    /// Returns the last N turns of a conversation, oldest first.
    /// Used to inject history into the LLM prompt.
    /// </summary>
    public async Task<List<ConversationTurn>> GetRecentTurnsAsync(
        string conversationId,
        int maxTurns = 5)
    {
        var table = LogsTable();
        await table.CreateIfNotExistsAsync();

        var safe    = conversationId.Replace("'", "''");
        var filter  = $"ConversationId eq '{safe}'";
        var results = new List<ConversationTurn>();

        await foreach (var e in table.QueryAsync<TableEntity>(filter))
        {
            var userMsg = GetStr(e, "UserMessage");
            var botAns  = GetStr(e, "BotAnswer");

            // Only include turns that have actual message content
            if (string.IsNullOrWhiteSpace(userMsg) || string.IsNullOrWhiteSpace(botAns))
                continue;

            results.Add(new ConversationTurn
            {
                UserMessage  = userMsg,
                BotAnswer    = botAns,
                CreatedAtUtc = GetStr(e, "CreatedAtUtc") ?? ""
            });
        }

        // Sort oldest first, then take last N turns
        results.Sort((a, b) => string.Compare(a.CreatedAtUtc, b.CreatedAtUtc, StringComparison.Ordinal));
        return results.Count > maxTurns
            ? results.GetRange(results.Count - maxTurns, maxTurns)
            : results;
    }

    public async Task<List<ConversationLogEntry>> GetConversationsByUsernameAsync(string username)
    {
        var table = LogsTable();
        await table.CreateIfNotExistsAsync();

        var safe = username.Replace("'", "''");
        var filter = $"Username eq '{safe}'";

        var results = new List<ConversationLogEntry>();
        await foreach (var e in table.QueryAsync<TableEntity>(filter))
        {
            results.Add(new ConversationLogEntry
            {
                RequestId      = GetStr(e, "RequestId")      ?? "",
                ConversationId = GetStr(e, "ConversationId") ?? "",
                Username       = GetStr(e, "Username")       ?? "",
                Outcome        = GetStr(e, "Outcome")        ?? "",
                IssueType      = GetStr(e, "RequestType")    ?? "",
                TicketId       = GetStr(e, "TicketId"),
                CreatedAtUtc   = GetStr(e, "CreatedAtUtc")   ?? ""
            });
        }

        results.Sort((a, b) => string.Compare(b.CreatedAtUtc, a.CreatedAtUtc, StringComparison.Ordinal));
        return results;
    }

    public async Task<DebugLookupResult?> GetDebugByRequestIdAsync(string requestId)
    {
        var table = LogsTable();
        await table.CreateIfNotExistsAsync();

        var safe = requestId.Replace("'", "''");
        var filter = $"RequestId eq '{safe}'";

        await foreach (var e in table.QueryAsync<TableEntity>(filter, maxPerPage: 50))
        {
            return new DebugLookupResult
            {
                PartitionKey = e.PartitionKey,
                RowKey = e.RowKey,
                RequestId = requestId,
                CreatedAtUtc = GetStr(e, "CreatedAtUtc"),
                ConversationId = GetStr(e, "ConversationId"),
                Outcome = GetStr(e, "Outcome"),
                Role = GetStr(e, "Role"),
                DataBoundary = GetStr(e, "DataBoundary"),
                RequestType = GetStr(e, "RequestType"),
                UsedRetrieval = GetStr(e, "UsedRetrieval"),
                TopScore = GetStr(e, "TopScore"),
                TicketId = GetStr(e, "TicketId"),
                LatencyMs = GetStr(e, "LatencyMs"),
                DebugJson = GetStr(e, "DebugJson")
            };
        }

        return null;
    }

    private static string? GetStr(TableEntity e, string name)
        => e.TryGetValue(name, out var v) ? v?.ToString() : null;

    public sealed class ConversationLogEntry
    {
        public string  RequestId      { get; set; } = "";
        public string  ConversationId { get; set; } = "";
        public string  Username       { get; set; } = "";
        public string  Outcome        { get; set; } = "";
        public string  IssueType      { get; set; } = "";
        public string? TicketId       { get; set; }
        public string  CreatedAtUtc   { get; set; } = "";
    }

    public sealed class ConversationTurn
    {
        public string UserMessage  { get; set; } = "";
        public string BotAnswer    { get; set; } = "";
        public string CreatedAtUtc { get; set; } = "";
    }

    public sealed class DebugLookupResult
    {
        public string? PartitionKey { get; set; }
        public string? RowKey { get; set; }
        public string RequestId { get; set; } = "";
        public string? CreatedAtUtc { get; set; }
        public string? ConversationId { get; set; }
        public string? Outcome { get; set; }
        public string? Role { get; set; }
        public string? DataBoundary { get; set; }
        public string? RequestType { get; set; }
        public string? UsedRetrieval { get; set; }
        public string? TopScore { get; set; }
        public string? TicketId { get; set; }
        public string? LatencyMs { get; set; }
        public string? DebugJson { get; set; }
    }
}