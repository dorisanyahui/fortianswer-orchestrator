using System.Text.Json;
using FortiAnswer.Orchestrator.Models;
using FortiAnswer.Orchestrator.Services;
using Microsoft.Extensions.Logging;

namespace FortiAnswer.Orchestrator.Tests;

/// <summary>
/// Integration tests for SlotSessionService — requires a live Azure Storage connection.
/// Connection string is read from c:\Dev\appsettings.azure.json (BLOB_CONNECTION key).
///
/// These tests use a separate table "SlotSessionsTest" to avoid touching production data.
/// Each test gets a unique conversationId so tests do not interfere with each other.
/// </summary>
[Trait("Category", "Integration")]
public class SlotSessionServiceTests
{
    private static readonly string? ConnectionString = TryReadConnectionString();

    private static string? TryReadConnectionString()
    {
        try
        {
            var json = File.ReadAllText(@"c:\Dev\appsettings.azure.json");
            var doc  = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("BLOB_CONNECTION").GetString();
        }
        catch
        {
            return null;
        }
    }

    private SlotSessionService CreateService()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
            throw new SkipException("BLOB_CONNECTION not available — skipping integration test.");

        Environment.SetEnvironmentVariable("SLOT_SESSIONS_TABLE", "SlotSessionsTest");

        var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
        var log = loggerFactory.CreateLogger<SlotSessionService>();
        return new SlotSessionService(ConnectionString, log);
    }

    private static string NewConvId() =>
        "test-" + Guid.NewGuid().ToString("N")[..12];

    // ── GetActive ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetActive_WhenNoSessionExists_ReturnsNull()
    {
        var svc = CreateService();

        var result = await svc.GetActiveAsync("nonexistent-conv-xyz-99999");

        Assert.Null(result);
    }

    // ── Start → GetActive ─────────────────────────────────────────────────────

    [Fact]
    public async Task Start_ThenGetActive_ReturnsCorrectSession()
    {
        var svc    = CreateService();
        var convId = NewConvId();

        await svc.StartAsync(convId, "Phishing", "alice", "Customer", "Public", "Got a suspicious email");

        var session = await svc.GetActiveAsync(convId);

        Assert.NotNull(session);
        Assert.Equal(convId,                     session.ConversationId);
        Assert.Equal("Phishing",                 session.IssueType);
        Assert.Equal("alice",                    session.Username);
        Assert.Equal("Customer",                 session.UserRole);
        Assert.Equal("Public",                   session.DataBoundary);
        Assert.Equal("Got a suspicious email",   session.OriginalMessage);
        Assert.Equal(0,                          session.CurrentSlotIndex);
        Assert.Equal("active",                   session.Status);
        Assert.Empty(session.CollectedSlots);
    }

    [Fact]
    public async Task Start_LongOriginalMessage_IsTruncatedTo500Chars()
    {
        var svc    = CreateService();
        var convId = NewConvId();
        var longMsg = new string('x', 600);

        await svc.StartAsync(convId, "VPN", "bob", "Agent", "Internal", longMsg);

        var session = await svc.GetActiveAsync(convId);
        Assert.NotNull(session);
        Assert.True(session.OriginalMessage.Length <= 500);
    }

    // ── SaveAnswerAndAdvance ──────────────────────────────────────────────────

    [Fact]
    public async Task SaveAnswerAndAdvance_IncreasesIndexInMemoryAndStorage()
    {
        var svc    = CreateService();
        var convId = NewConvId();

        await svc.StartAsync(convId, "VPN", "carol", "Customer", "Public", "VPN broken");

        var session = await svc.GetActiveAsync(convId);
        Assert.NotNull(session);
        Assert.Equal(0, session.CurrentSlotIndex);

        await svc.SaveAnswerAndAdvanceAsync(session, "vpnClient", "GlobalProtect");

        // In-memory mutation
        Assert.Equal(1, session.CurrentSlotIndex);
        Assert.Equal("GlobalProtect", session.CollectedSlots["vpnClient"]);

        // Persisted in storage
        var reloaded = await svc.GetActiveAsync(convId);
        Assert.NotNull(reloaded);
        Assert.Equal(1, reloaded.CurrentSlotIndex);
        Assert.Equal("GlobalProtect", reloaded.CollectedSlots["vpnClient"]);
    }

    [Fact]
    public async Task SaveAnswerAndAdvance_MultipleAnswers_AccumulateCorrectly()
    {
        var svc    = CreateService();
        var convId = NewConvId();

        await svc.StartAsync(convId, "MFA", "dave", "Customer", "Public", "MFA not working");

        var session = await svc.GetActiveAsync(convId);
        Assert.NotNull(session);

        await svc.SaveAnswerAndAdvanceAsync(session, "mfaMethod", "Authenticator app");
        await svc.SaveAnswerAndAdvanceAsync(session, "deviceType", "iPhone 15");

        Assert.Equal(2, session.CurrentSlotIndex);
        Assert.Equal(2, session.CollectedSlots.Count);

        var reloaded = await svc.GetActiveAsync(convId);
        Assert.NotNull(reloaded);
        Assert.Equal(2,                    reloaded.CollectedSlots.Count);
        Assert.Equal("Authenticator app",  reloaded.CollectedSlots["mfaMethod"]);
        Assert.Equal("iPhone 15",          reloaded.CollectedSlots["deviceType"]);
    }

    // ── Complete ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Complete_ThenGetActive_ReturnsNull()
    {
        var svc    = CreateService();
        var convId = NewConvId();

        await svc.StartAsync(convId, "MFA", "eve", "Customer", "Public", "Can't login");
        await svc.CompleteAsync(convId);

        var result = await svc.GetActiveAsync(convId);

        Assert.Null(result);
    }

    // ── Full flow ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("AccountLockout", 3)]
    [InlineData("PasswordReset",  3)]
    [InlineData("Phishing",       4)]
    [InlineData("VPN",            4)]
    public async Task FullFlow_CollectsAllSlotsThenCompletes(string issueType, int totalSlots)
    {
        var svc    = CreateService();
        var convId = NewConvId();
        var slots  = SlotDefinitions.GetSlots(issueType);

        Assert.Equal(totalSlots, slots.Count);

        // Start session
        await svc.StartAsync(convId, issueType, "flowuser", "Customer", "Public", "Test message");

        // Answer each slot one by one (simulating chat turns)
        for (int i = 0; i < slots.Count; i++)
        {
            var session = await svc.GetActiveAsync(convId);
            Assert.NotNull(session);
            Assert.Equal(i, session.CurrentSlotIndex);

            await svc.SaveAnswerAndAdvanceAsync(session, slots[i].Key, $"answer-{i}");
        }

        // After all slots: index == totalSlots, session still active
        var finalSession = await svc.GetActiveAsync(convId);
        Assert.NotNull(finalSession);
        Assert.Equal(totalSlots, finalSession.CurrentSlotIndex);
        Assert.Equal(totalSlots, finalSession.CollectedSlots.Count);

        for (int i = 0; i < slots.Count; i++)
            Assert.Equal($"answer-{i}", finalSession.CollectedSlots[slots[i].Key]);

        // Complete
        await svc.CompleteAsync(convId);
        Assert.Null(await svc.GetActiveAsync(convId));
    }

    [Fact]
    public async Task Start_Twice_OverwritesPreviousSession()
    {
        var svc    = CreateService();
        var convId = NewConvId();

        await svc.StartAsync(convId, "Phishing", "user1", "Customer", "Public", "First message");
        var s1 = await svc.GetActiveAsync(convId);
        Assert.NotNull(s1);

        // Simulate restart (e.g., session expired and user re-triggered)
        await svc.StartAsync(convId, "VPN", "user2", "Agent", "Internal", "Second message");

        var s2 = await svc.GetActiveAsync(convId);
        Assert.NotNull(s2);
        Assert.Equal("VPN",            s2.IssueType);
        Assert.Equal("user2",          s2.Username);
        Assert.Equal(0,                s2.CurrentSlotIndex);
        Assert.Empty(s2.CollectedSlots);
    }
}

/// <summary>
/// xUnit v2 does not have a built-in Skip mechanism for facts/theories.
/// Throw this to signal that a test should be skipped (shows as Failed with message,
/// which is acceptable for an integration gate in this project).
/// </summary>
internal sealed class SkipException : Exception
{
    public SkipException(string reason) : base($"SKIP: {reason}") { }
}
