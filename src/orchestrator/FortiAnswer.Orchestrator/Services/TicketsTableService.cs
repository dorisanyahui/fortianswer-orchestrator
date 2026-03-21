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
    /// Returns all tickets across all users, with optional client-side filters. Newest first.
    /// Intended for Agent / Admin callers only — enforce role check in the Function layer.
    /// </summary>
    public async Task<List<TicketEntity>> GetAllAsync(
        string? status       = null,
        string? priority     = null,
        string? issueType    = null,
        string? assignedTo   = null,
        string? dataBoundary = null)
    {
        // All tickets share PartitionKey "ticket" — fetch the whole partition and filter in memory.
        var results = new List<TicketEntity>();
        await foreach (var e in _table.QueryAsync<TableEntity>("PartitionKey eq 'ticket'"))
        {
            results.Add(MapEntity(e));
        }

        if (!string.IsNullOrWhiteSpace(status))
            results = results.Where(t => string.Equals(t.Status, status, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!string.IsNullOrWhiteSpace(priority))
            results = results.Where(t => string.Equals(t.Priority, priority, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!string.IsNullOrWhiteSpace(issueType))
            results = results.Where(t => string.Equals(t.IssueType, issueType, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!string.IsNullOrWhiteSpace(assignedTo))
            results = results.Where(t => string.Equals(t.AssignedTo, assignedTo, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!string.IsNullOrWhiteSpace(dataBoundary))
            results = results.Where(t => string.Equals(t.DataBoundary, dataBoundary, StringComparison.OrdinalIgnoreCase)).ToList();

        results.Sort((a, b) => string.Compare(b.CreatedUtc, a.CreatedUtc, StringComparison.Ordinal));
        return results;
    }

    /// <summary>
    /// Updates mutable fields on an existing ticket.
    /// Only non-null values in the request are applied.
    /// Returns false if the ticket does not exist.
    /// </summary>
    public async Task<bool> UpdateAsync(string ticketId, TicketUpdateRequest update)
    {
        var res = await _table.GetEntityIfExistsAsync<TableEntity>("ticket", ticketId);
        if (!res.HasValue || res.Value is null) return false;

        var e = res.Value;

        if (update.Status is not null)
            e["Status"] = update.Status;
        if (update.AssignedTo is not null)
            e["AssignedTo"] = update.AssignedTo;
        if (update.Priority is not null)
            e["Priority"] = update.Priority;

        e["UpdatedUtc"] = DateTimeOffset.UtcNow.ToString("o");

        await _table.UpdateEntityAsync(e, e.ETag, TableUpdateMode.Replace);
        return true;
    }

    /// <summary>
    /// Returns all tickets created by the given username, newest first.
    /// </summary>
    public async Task<List<TicketEntity>> GetByUsernameAsync(string username)
    {
        var safe = username.Replace("'", "''");
        var filter = $"CreatedByUser eq '{safe}'";

        var results = new List<TicketEntity>();
        await foreach (var e in _table.QueryAsync<TableEntity>(filter))
            results.Add(MapEntity(e));

        results.Sort((a, b) => string.Compare(b.CreatedUtc, a.CreatedUtc, StringComparison.Ordinal));
        return results;
    }

    /// <summary>
    /// Returns a ticket by ID, or null if not found.
    /// </summary>
    public async Task<TicketEntity?> GetByIdAsync(string ticketId)
    {
        var res = await _table.GetEntityIfExistsAsync<TableEntity>("ticket", ticketId);
        if (!res.HasValue || res.Value is null) return null;
        return MapEntity(res.Value, ticketId);
    }

    private static TicketEntity MapEntity(TableEntity e, string? idOverride = null) => new()
    {
        TicketId         = idOverride ?? GetStr(e, "TicketId") ?? e.RowKey,
        ConversationId   = GetStr(e, "ConversationId"),
        Status           = GetStr(e, "Status")           ?? "Open",
        Priority         = GetStr(e, "Priority")         ?? "P4",
        IssueType        = GetStr(e, "IssueType")        ?? "General",
        DataBoundary     = GetStr(e, "DataBoundary")     ?? "Public",
        CreatedByUser    = GetStr(e, "CreatedByUser")    ?? "",
        AssignedTo       = GetStr(e, "AssignedTo"),
        Summary          = GetStr(e, "Summary")          ?? "",
        EscalationReason = GetStr(e, "EscalationReason") ?? "",
        Source           = GetStr(e, "Source")           ?? "manual",
        CreatedUtc       = GetStr(e, "CreatedUtc")       ?? "",
        UpdatedUtc       = GetStr(e, "UpdatedUtc")       ?? ""
    };

    /// <summary>
    /// Lightweight connectivity check — tries to read a single entity.
    /// Returns true if Table Storage is reachable, false on any error.
    /// </summary>
    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        try
        {
            await _table.GetEntityIfExistsAsync<TableEntity>("ticket", "__ping__", cancellationToken: ct);
            return true;
        }
        catch { return false; }
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
    public string  Status           { get; set; } = "Open";   // Open | InProgress | Closed
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

/// <summary>
/// Payload for PATCH /api/tickets/{id}. All fields are optional — only non-null values are applied.
/// </summary>
public sealed class TicketUpdateRequest
{
    /// <summary>Open | InProgress | Closed</summary>
    public string? Status     { get; set; }

    /// <summary>Username of the agent taking ownership.</summary>
    public string? AssignedTo { get; set; }

    /// <summary>P1 | P2 | P3 | P4 — allow manual override.</summary>
    public string? Priority   { get; set; }
}
