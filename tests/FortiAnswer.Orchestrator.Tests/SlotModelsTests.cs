using FortiAnswer.Orchestrator.Models;

namespace FortiAnswer.Orchestrator.Tests;

/// <summary>
/// Pure unit tests for the slot model classes — no I/O, no dependencies.
/// </summary>
public class SlotModelsTests
{
    // ── SlotDefinition ───────────────────────────────────────────────────────

    [Fact]
    public void SlotDefinition_Constructor_SetsAllProperties()
    {
        var slot = new SlotDefinition("myKey", "My question?", "e.g. something");

        Assert.Equal("myKey",        slot.Key);
        Assert.Equal("My question?", slot.Question);
        Assert.Equal("e.g. something", slot.Hint);
    }

    // ── SlotSession ──────────────────────────────────────────────────────────

    [Fact]
    public void SlotSession_Defaults_AreCorrect()
    {
        var session = new SlotSession();

        Assert.Equal("",      session.ConversationId);
        Assert.Equal("",      session.IssueType);
        Assert.Equal("",      session.Username);
        Assert.Equal("",      session.UserRole);
        Assert.Equal("",      session.DataBoundary);
        Assert.Equal("",      session.OriginalMessage);
        Assert.Equal(0,       session.CurrentSlotIndex);
        Assert.Equal("active", session.Status);
        Assert.Empty(session.CollectedSlots);
    }

    [Fact]
    public void SlotSession_CollectedSlots_CanBeAddedTo()
    {
        var session = new SlotSession { IssueType = "Phishing" };
        session.CollectedSlots["senderEmail"] = "evil@evil.com";
        session.CollectedSlots["clickedLink"] = "yes";

        Assert.Equal(2, session.CollectedSlots.Count);
        Assert.Equal("evil@evil.com", session.CollectedSlots["senderEmail"]);
    }

    [Fact]
    public void SlotSession_CurrentSlotIndex_CanBeIncremented()
    {
        var session = new SlotSession { IssueType = "VPN", CurrentSlotIndex = 0 };
        session.CurrentSlotIndex++;

        Assert.Equal(1, session.CurrentSlotIndex);
    }

    // ── SlotFillingInfo ──────────────────────────────────────────────────────

    [Fact]
    public void SlotFillingInfo_ActiveState_SetsAllFields()
    {
        var info = new SlotFillingInfo
        {
            IsActive     = true,
            CurrentStep  = 2,
            TotalSteps   = 4,
            NextQuestion = "What device did you receive this on?",
            SlotKey      = "affectedDevice",
            Hint         = "e.g. MacBook, Windows laptop",
        };

        Assert.True(info.IsActive);
        Assert.Equal(2, info.CurrentStep);
        Assert.Equal(4, info.TotalSteps);
        Assert.Equal("What device did you receive this on?", info.NextQuestion);
        Assert.Equal("affectedDevice", info.SlotKey);
        Assert.Equal("e.g. MacBook, Windows laptop", info.Hint);
    }

    [Fact]
    public void SlotFillingInfo_InactiveState_IsActiveIsFalse()
    {
        var info = new SlotFillingInfo { IsActive = false };

        Assert.False(info.IsActive);
        Assert.Null(info.NextQuestion);
        Assert.Null(info.SlotKey);
        Assert.Null(info.Hint);
        Assert.Equal(0, info.CurrentStep);
        Assert.Equal(0, info.TotalSteps);
    }

    // ── Slot progress calculation ─────────────────────────────────────────────

    [Theory]
    [InlineData(0, 4, true,  1)]   // index 0 → step 1, more slots remain
    [InlineData(1, 4, true,  2)]   // index 1 → step 2
    [InlineData(3, 4, true,  4)]   // index 3 → step 4 (last question)
    [InlineData(4, 4, false, 5)]   // index 4 → all done (index == totalSlots)
    public void SlotProgress_Calculation_IsCorrect(
        int currentIndex, int totalSlots, bool moreRemaining, int expectedStep)
    {
        bool hasMore = currentIndex < totalSlots;
        int  step    = currentIndex + 1;

        Assert.Equal(moreRemaining, hasMore);
        Assert.Equal(expectedStep,  step);
    }
}
