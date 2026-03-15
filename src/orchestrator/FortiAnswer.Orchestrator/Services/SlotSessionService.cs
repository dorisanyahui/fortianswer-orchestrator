using System.Text.Json;
using Azure.Data.Tables;
using FortiAnswer.Orchestrator.Models;
using Microsoft.Extensions.Logging;

namespace FortiAnswer.Orchestrator.Services;

/// <summary>
/// Manages per-conversation slot-filling sessions in Azure Table Storage.
///
/// Table: SlotSessions (configurable via SLOT_SESSIONS_TABLE env var)
///   PartitionKey = "slot"
///   RowKey       = conversationId
///
/// Sessions expire after 30 minutes of inactivity (checked on read).
/// </summary>
public sealed class SlotSessionService
{
    private readonly TableClient _table;
    private readonly ILogger<SlotSessionService> _log;

    private static readonly TimeSpan SessionTtl = TimeSpan.FromMinutes(30);

    public SlotSessionService(string storageConnectionString, ILogger<SlotSessionService> log)
    {
        _log = log;
        var tableName = Environment.GetEnvironmentVariable("SLOT_SESSIONS_TABLE") ?? "SlotSessions";
        _table = new TableClient(storageConnectionString, tableName);
        _table.CreateIfNotExists();
    }

    // ── Read ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the active, non-expired slot session for a conversation, or null.
    /// Automatically marks the session as expired in storage if the TTL has passed.
    /// </summary>
    public async Task<SlotSession?> GetActiveAsync(string conversationId)
    {
        try
        {
            var result = await _table.GetEntityIfExistsAsync<TableEntity>("slot", conversationId);
            if (!result.HasValue || result.Value is null) return null;

            var e = result.Value;
            var status = GetStr(e, "Status") ?? "";
            if (status != "active") return null;

            // TTL check
            var updatedStr = GetStr(e, "UpdatedUtc") ?? "";
            if (DateTimeOffset.TryParse(updatedStr, out var updated) &&
                DateTimeOffset.UtcNow - updated > SessionTtl)
            {
                _log.LogInformation("SlotSession expired for conversationId={ConversationId}", conversationId);
                await SetStatusAsync(conversationId, "expired");
                return null;
            }

            var collectedJson = GetStr(e, "CollectedSlotsJson") ?? "{}";
            Dictionary<string, string> collected;
            try   { collected = JsonSerializer.Deserialize<Dictionary<string, string>>(collectedJson) ?? new(); }
            catch { collected = new(); }

            return new SlotSession
            {
                ConversationId   = conversationId,
                IssueType        = GetStr(e, "IssueType")        ?? "General",
                Username         = GetStr(e, "Username")         ?? "anonymous",
                UserRole         = GetStr(e, "UserRole")         ?? "Customer",
                DataBoundary     = GetStr(e, "DataBoundary")     ?? "Public",
                OriginalMessage  = GetStr(e, "OriginalMessage")  ?? "",
                CollectedSlots   = collected,
                CurrentSlotIndex = int.TryParse(GetStr(e, "CurrentSlotIndex"), out var idx) ? idx : 0,
                Status           = status,
                CreatedUtc       = DateTimeOffset.TryParse(GetStr(e, "CreatedUtc"), out var c) ? c : DateTimeOffset.UtcNow,
                UpdatedUtc       = DateTimeOffset.TryParse(GetStr(e, "UpdatedUtc"), out var u) ? u : DateTimeOffset.UtcNow,
            };
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to read slot session for conversationId={ConversationId}", conversationId);
            return null;
        }
    }

    // ── Write ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates (or overwrites) an active slot session for a conversation.
    /// Always resets to slot index 0 and clears any previously collected answers.
    /// </summary>
    public async Task StartAsync(
        string conversationId,
        string issueType,
        string username,
        string userRole,
        string dataBoundary,
        string originalMessage)
    {
        var now = DateTimeOffset.UtcNow.ToString("o");
        var entity = new TableEntity("slot", conversationId)
        {
            ["IssueType"]          = issueType,
            ["Username"]           = username,
            ["UserRole"]           = userRole,
            ["DataBoundary"]       = dataBoundary,
            ["OriginalMessage"]    = originalMessage[..Math.Min(500, originalMessage.Length)],
            ["CollectedSlotsJson"] = "{}",
            ["CurrentSlotIndex"]   = "0",
            ["Status"]             = "active",
            ["CreatedUtc"]         = now,
            ["UpdatedUtc"]         = now,
        };
        await _table.UpsertEntityAsync(entity);
        _log.LogInformation("SlotSession started for conversationId={ConversationId} issueType={IssueType}", conversationId, issueType);
    }

    /// <summary>
    /// Saves the answer for the current slot and advances CurrentSlotIndex by 1.
    /// Mutates the in-memory session object so the caller can read the new index immediately.
    /// </summary>
    public async Task SaveAnswerAndAdvanceAsync(SlotSession session, string slotKey, string answer)
    {
        session.CollectedSlots[slotKey] = answer;
        session.CurrentSlotIndex++;
        session.UpdatedUtc = DateTimeOffset.UtcNow;

        var entity = new TableEntity("slot", session.ConversationId)
        {
            ["IssueType"]          = session.IssueType,
            ["Username"]           = session.Username,
            ["UserRole"]           = session.UserRole,
            ["DataBoundary"]       = session.DataBoundary,
            ["OriginalMessage"]    = session.OriginalMessage,
            ["CollectedSlotsJson"] = JsonSerializer.Serialize(session.CollectedSlots),
            ["CurrentSlotIndex"]   = session.CurrentSlotIndex.ToString(),
            ["Status"]             = "active",
            ["CreatedUtc"]         = session.CreatedUtc.ToString("o"),
            ["UpdatedUtc"]         = session.UpdatedUtc.ToString("o"),
        };
        await _table.UpsertEntityAsync(entity);
    }

    /// <summary>Marks a session as complete (ticket was created).</summary>
    public async Task CompleteAsync(string conversationId)
        => await SetStatusAsync(conversationId, "complete");

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task SetStatusAsync(string conversationId, string status)
    {
        try
        {
            var result = await _table.GetEntityIfExistsAsync<TableEntity>("slot", conversationId);
            if (!result.HasValue || result.Value is null) return;

            var entity = result.Value;
            entity["Status"]     = status;
            entity["UpdatedUtc"] = DateTimeOffset.UtcNow.ToString("o");
            await _table.UpsertEntityAsync(entity);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to set slot session status={Status} for conversationId={ConversationId}", status, conversationId);
        }
    }

    private static string? GetStr(TableEntity e, string name)
        => e.TryGetValue(name, out var v) ? v?.ToString() : null;
}
