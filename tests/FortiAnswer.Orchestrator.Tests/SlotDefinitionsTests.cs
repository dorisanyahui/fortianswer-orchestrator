using FortiAnswer.Orchestrator.Services;

namespace FortiAnswer.Orchestrator.Tests;

/// <summary>
/// Pure unit tests for SlotDefinitions — no I/O, no dependencies.
/// </summary>
public class SlotDefinitionsTests
{
    // ── HasSlots ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Phishing",        true)]
    [InlineData("SuspiciousLogin", true)]
    [InlineData("VPN",             true)]
    [InlineData("MFA",             true)]
    [InlineData("EndpointAlert",   true)]
    [InlineData("AccountLockout",  true)]
    [InlineData("PasswordReset",   true)]
    [InlineData("General",         false)]
    [InlineData("Severity",        false)]
    [InlineData("",                false)]
    [InlineData(null,              false)]
    public void HasSlots_ReturnsExpected(string? issueType, bool expected)
    {
        Assert.Equal(expected, SlotDefinitions.HasSlots(issueType));
    }

    // ── GetTotalSlots ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Phishing",        4)]
    [InlineData("SuspiciousLogin", 4)]
    [InlineData("VPN",             4)]
    [InlineData("MFA",             4)]
    [InlineData("EndpointAlert",   4)]
    [InlineData("AccountLockout",  3)]
    [InlineData("PasswordReset",   3)]
    [InlineData("General",         0)]
    [InlineData("Unknown",         0)]
    public void GetTotalSlots_ReturnsCorrectCount(string issueType, int expected)
    {
        Assert.Equal(expected, SlotDefinitions.GetTotalSlots(issueType));
    }

    // ── GetSlots ──────────────────────────────────────────────────────────────

    [Fact]
    public void GetSlots_UnknownType_ReturnsEmptyList()
    {
        var slots = SlotDefinitions.GetSlots("UnknownType");
        Assert.Empty(slots);
    }

    [Theory]
    [InlineData("phishing")]
    [InlineData("PHISHING")]
    [InlineData("Phishing")]
    [InlineData("pHiShInG")]
    public void GetSlots_IsCaseInsensitive(string issueType)
    {
        var slots = SlotDefinitions.GetSlots(issueType);
        Assert.Equal(4, slots.Count);
    }

    // ── Slot content quality ─────────────────────────────────────────────────

    [Theory]
    [InlineData("Phishing")]
    [InlineData("SuspiciousLogin")]
    [InlineData("VPN")]
    [InlineData("MFA")]
    [InlineData("EndpointAlert")]
    [InlineData("AccountLockout")]
    [InlineData("PasswordReset")]
    public void AllSlots_HaveNonEmptyKeyQuestionAndHint(string issueType)
    {
        var slots = SlotDefinitions.GetSlots(issueType);

        foreach (var slot in slots)
        {
            Assert.False(string.IsNullOrWhiteSpace(slot.Key),      $"{issueType}: slot has empty Key");
            Assert.False(string.IsNullOrWhiteSpace(slot.Question), $"{issueType}: slot has empty Question");
            Assert.False(string.IsNullOrWhiteSpace(slot.Hint),     $"{issueType}: slot has empty Hint");
        }
    }

    [Theory]
    [InlineData("Phishing")]
    [InlineData("SuspiciousLogin")]
    [InlineData("VPN")]
    [InlineData("MFA")]
    [InlineData("EndpointAlert")]
    [InlineData("AccountLockout")]
    [InlineData("PasswordReset")]
    public void AllSlots_HaveUniqueKeysWithinType(string issueType)
    {
        var slots = SlotDefinitions.GetSlots(issueType);
        var keys  = slots.Select(s => s.Key).ToList();

        Assert.Equal(keys.Count, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void FirstSlot_Phishing_IsAboutSenderEmail()
    {
        var first = SlotDefinitions.GetSlots("Phishing")[0];

        Assert.Equal("senderEmail", first.Key);
        Assert.Contains("sender", first.Question, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FirstSlot_SuspiciousLogin_IsAboutAffectedAccount()
    {
        var first = SlotDefinitions.GetSlots("SuspiciousLogin")[0];

        Assert.Equal("affectedAccount", first.Key);
    }

    [Fact]
    public void GetSlots_ReturnsSameReferenceAcrossCalls()
    {
        // Verify no accidental mutation — both calls should return the same content.
        var a = SlotDefinitions.GetSlots("VPN");
        var b = SlotDefinitions.GetSlots("VPN");

        Assert.Equal(a.Count, b.Count);
        for (int i = 0; i < a.Count; i++)
            Assert.Equal(a[i].Key, b[i].Key);
    }
}
