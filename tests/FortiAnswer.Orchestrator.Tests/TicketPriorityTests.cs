using FortiAnswer.Orchestrator.Services;

namespace FortiAnswer.Orchestrator.Tests;

/// <summary>
/// Unit tests for TicketsTableService.DerivePriority — pure static method, no I/O.
/// </summary>
public class TicketPriorityTests
{
    [Theory]
    [InlineData("Phishing",        "P1")]
    [InlineData("SuspiciousLogin", "P1")]
    [InlineData("Severity",        "P1")]
    [InlineData("EndpointAlert",   "P2")]
    [InlineData("AccountLockout",  "P2")]
    [InlineData("VPN",             "P3")]
    [InlineData("MFA",             "P3")]
    [InlineData("PasswordReset",   "P3")]
    [InlineData("General",         "P4")]
    [InlineData(null,              "P4")]
    [InlineData("",                "P4")]
    [InlineData("UnknownType",     "P4")]
    public void DerivePriority_ReturnsExpected(string? issueType, string expected)
    {
        Assert.Equal(expected, TicketsTableService.DerivePriority(issueType));
    }

    [Fact]
    public void DerivePriority_Phishing_IsHigherPriorityThanVPN()
    {
        // P1 numerically lower = higher urgency
        var phishing = TicketsTableService.DerivePriority("Phishing");
        var vpn      = TicketsTableService.DerivePriority("VPN");

        Assert.True(string.Compare(phishing, vpn, StringComparison.Ordinal) < 0,
            "P1 should sort before P3");
    }

    [Fact]
    public void DerivePriority_SlotFillingIssueTypes_MapToExpectedPriority()
    {
        // All issueTypes that have slot definitions should map to a known priority.
        var slotTypes = new[]
        {
            ("Phishing",        "P1"),
            ("SuspiciousLogin", "P1"),
            ("VPN",             "P3"),
            ("MFA",             "P3"),
            ("EndpointAlert",   "P2"),
            ("AccountLockout",  "P2"),
            ("PasswordReset",   "P3"),
        };

        foreach (var (type, expected) in slotTypes)
            Assert.Equal(expected, TicketsTableService.DerivePriority(type));
    }
}
