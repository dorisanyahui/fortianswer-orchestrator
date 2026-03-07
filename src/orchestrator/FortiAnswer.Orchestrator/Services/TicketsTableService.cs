using Azure.Data.Tables;

namespace FortiAnswer.Orchestrator.Services;

public sealed class TicketsTableService
{
    private readonly TableClient _table;

    public TicketsTableService(string storageConnectionString)
    {
        var tableName = Environment.GetEnvironmentVariable("TICKETS_TABLE_NAME") ?? "Tickets";
        _table = new TableClient(storageConnectionString, tableName);
        _table.CreateIfNotExists();
    }

    /// <summary>
    /// Creates a new ticket and returns the generated ticketId.
    /// </summary>
    public async Task<string> CreateAsync(
        string? conversationId,
        string createdByUser,
        string issueType,
        string dataBoundary,
        string summary,
        string escalationReason,
        string source) // "auto" | "manual"
    {
        var ticketId = Guid.NewGuid().ToString("N")[..12];
        var priority = DerivePriority(issueType);
        var now = DateTimeOffset.UtcNow.ToString("o");

        var entity = new TableEntity("ticket", ticketId)
        {
            ["TicketId"]          = ticketId,
            ["ConversationId"]    = conversationId ?? "",
            ["Status"]            = "Open",
            ["Priority"]          = priority,
            ["IssueType"]         = issueType,
            ["DataBoundary"]      = dataBoundary,
            ["CreatedByUser"]     = createdByUser,
            ["AssignedTo"]        = "",
            ["Summary"]           = summary,
            ["EscalationReason"]  = escalationReason,
            ["Source"]            = source,
            ["CreatedUtc"]        = now,
            ["UpdatedUtc"]        = now
        };

        await _table.AddEntityAsync(entity);
        return ticketId;
    }

    /// <summary>
    /// Returns a ticket by ID, or null if not found.
    /// </summary>
    public async Task<TicketEntity?> GetByIdAsync(string ticketId)
    {
        var res = await _table.GetEntityIfExistsAsync<TableEntity>("ticket", ticketId);
        if (!res.HasValue || res.Value is null) return null;

        var e = res.Value;
        return new TicketEntity
        {
            TicketId          = ticketId,
            ConversationId    = GetStr(e, "ConversationId"),
            Status            = GetStr(e, "Status")           ?? "Open",
            Priority          = GetStr(e, "Priority")         ?? "P4",
            IssueType         = GetStr(e, "IssueType")        ?? "General",
            DataBoundary      = GetStr(e, "DataBoundary")     ?? "Public",
            CreatedByUser     = GetStr(e, "CreatedByUser")    ?? "",
            AssignedTo        = GetStr(e, "AssignedTo"),
            Summary           = GetStr(e, "Summary")          ?? "",
            EscalationReason  = GetStr(e, "EscalationReason") ?? "",
            Source            = GetStr(e, "Source")           ?? "manual",
            CreatedUtc        = GetStr(e, "CreatedUtc")       ?? "",
            UpdatedUtc        = GetStr(e, "UpdatedUtc")       ?? ""
        };
    }

    /// <summary>
    /// Maps issueType to ticket priority.
    /// </summary>
    public static string DerivePriority(string? issueType) =>
        (issueType ?? "General").ToLowerInvariant() switch
        {
            "phishing" or "suspiciouslogin" or "severity" => "P1",
            "endpointalert" or "accountlockout"            => "P2",
            "vpn" or "mfa" or "passwordreset"             => "P3",
            _                                              => "P4"
        };

    private static string? GetStr(TableEntity e, string name)
        => e.TryGetValue(name, out var v) ? v?.ToString() : null;
}

/// <summary>
/// Represents a ticket row returned from Table Storage.
/// </summary>
public sealed class TicketEntity
{
    public string  TicketId         { get; set; } = "";
    public string? ConversationId   { get; set; }
    public string  Status           { get; set; } = "Open";
    public string  Priority         { get; set; } = "P4";
    public string  IssueType        { get; set; } = "General";
    public string  DataBoundary     { get; set; } = "Public";
    public string  CreatedByUser    { get; set; } = "";
    public string? AssignedTo       { get; set; }
    public string  Summary          { get; set; } = "";
    public string  EscalationReason { get; set; } = "";
    public string  Source           { get; set; } = "manual";
    public string  CreatedUtc       { get; set; } = "";
    public string  UpdatedUtc       { get; set; } = "";
}
