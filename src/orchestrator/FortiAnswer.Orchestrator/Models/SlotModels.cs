namespace FortiAnswer.Orchestrator.Models;

/// <summary>
/// Defines a single slot (a required piece of information) for a given issueType.
/// </summary>
public sealed class SlotDefinition
{
    public string Key { get; }       // e.g. "senderEmail"
    public string Question { get; }  // text shown to the user
    public string Hint { get; }      // input placeholder / example

    public SlotDefinition(string key, string question, string hint)
    {
        Key      = key;
        Question = question;
        Hint     = hint;
    }
}

/// <summary>
/// Persisted slot-filling session stored in Azure Table Storage (SlotSessions table).
/// One row per conversationId; hydrated by SlotSessionService.
/// </summary>
public sealed class SlotSession
{
    public string ConversationId    { get; set; } = "";
    public string IssueType         { get; set; } = "";
    public string Username          { get; set; } = "";
    public string UserRole          { get; set; } = "";
    public string DataBoundary      { get; set; } = "";
    public string OriginalMessage   { get; set; } = "";

    /// <summary>Answers collected so far, keyed by SlotDefinition.Key.</summary>
    public Dictionary<string, string> CollectedSlots { get; set; } = new();

    /// <summary>0-based index of the slot to ask next.</summary>
    public int CurrentSlotIndex { get; set; } = 0;

    /// <summary>"active" | "complete" | "expired"</summary>
    public string Status { get; set; } = "active";

    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
}

/// <summary>
/// Included in ChatResponse when the backend is actively collecting slot data.
/// UI reads this to render guided questions and a progress indicator.
/// </summary>
public sealed class SlotFillingInfo
{
    /// <summary>True while there are still slots to collect.</summary>
    public bool IsActive { get; set; }

    /// <summary>The question the user must answer next (null when IsActive = false).</summary>
    public string? NextQuestion { get; set; }

    /// <summary>Machine-readable slot key — useful for client-side field validation hints.</summary>
    public string? SlotKey { get; set; }

    /// <summary>Suggested input placeholder / example answer.</summary>
    public string? Hint { get; set; }

    /// <summary>1-based step number of the current question.</summary>
    public int CurrentStep { get; set; }

    /// <summary>Total number of slots for this issueType.</summary>
    public int TotalSteps { get; set; }
}
