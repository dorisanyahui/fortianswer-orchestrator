namespace FortiAnswer.Orchestrator.Models;

public sealed class ChatRequest
{
    public string? Message { get; set; }
    public string? RequestType { get; set; }
    public string? UserRole { get; set; }
    public string? UserGroup { get; set; }
    public string? ConversationId { get; set; }
}

public sealed class Citation
{
    public string? Title { get; set; }
    public string? UrlOrId { get; set; }
    public string? Snippet { get; set; }
}

public sealed class EscalationInfo
{
    public bool ShouldEscalate { get; set; }
    public string? Reason { get; set; }
}

public sealed class ChatResponse
{
    public string Answer { get; set; } = "";
    public List<Citation> Citations { get; set; } = new();
    public List<string> ActionHints { get; set; } = new();
    public string RequestId { get; set; } = "";
    public EscalationInfo Escalation { get; set; } = new();
}
