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
        string outcome,
        string role,
        string dataBoundary,
        string requestType,
        string usedRetrieval,
        double? topScore,
        string? ticketId,
        long? latencyMs,
        object? debugObj)
    {
        var table = LogsTable();
        await table.CreateIfNotExistsAsync();

        var pk = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var rk = $"{DateTime.UtcNow:HHmmssfff}-{Guid.NewGuid():N}";

        var entity = new TableEntity(pk, rk)
        {
            ["CreatedAtUtc"] = DateTime.UtcNow.ToString("o"),
            ["RequestId"] = requestId,
            ["ConversationId"] = conversationId,
            ["Outcome"] = outcome,
            ["Role"] = role,
            ["DataBoundary"] = dataBoundary,
            ["RequestType"] = requestType,
            ["UsedRetrieval"] = usedRetrieval,
            ["TopScore"] = topScore is null ? null : topScore.Value.ToString("0.####"),
            ["TicketId"] = ticketId,
            ["LatencyMs"] = latencyMs is null ? null : latencyMs.Value.ToString(),
            ["DebugJson"] = debugObj is null ? null : JsonSerializer.Serialize(debugObj)
        };

        await table.AddEntityAsync(entity);

        _log.LogInformation("ConversationLog written. requestId={RequestId} PK={PK} RK={RK}", requestId, pk, rk);
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