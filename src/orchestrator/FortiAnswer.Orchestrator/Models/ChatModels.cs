namespace FortiAnswer.Orchestrator.Models;

/// <summary>
/// Incoming request body for POST /api/chat
/// </summary>
public sealed class ChatRequest
{
    public string? Message { get; set; }

    /// <summary>
    /// e.g. "Public" | "Internal"
    /// </summary>
    public string? RequestType { get; set; }

    /// <summary>
    /// e.g. "User" | "Agent" | "Admin"
    /// </summary>
    public string? UserRole { get; set; }

    /// <summary>
    /// Optional group name / role group for data boundary filtering
    /// </summary>
    public string? UserGroup { get; set; }

    /// <summary>
    /// Optional client-side conversation/thread id
    /// </summary>
    public string? ConversationId { get; set; }
}

/// <summary>
/// Evidence snippet returned from Retrieval
/// </summary>
public sealed class Citation
{
    public string? Title { get; set; }
    public string? UrlOrId { get; set; }
    public string? Snippet { get; set; }
}

/// <summary>
/// Escalation signal (future: ticket creation, human handoff, etc.)
/// </summary>
public sealed class EscalationInfo
{
    public bool ShouldEscalate { get; set; }
    public string? Reason { get; set; }
}

/// <summary>
/// Optional: report which mode was used. Safe to keep for future integration.
/// </summary>
public sealed class ModeInfo
{
    public string Retrieval { get; set; } = "stub"; // stub | azureaisearch | ...
    public string Llm { get; set; } = "stub";       // stub | azureopenai | ...
}

/// <summary>
/// Success response for POST /api/chat
/// </summary>
public sealed class ChatResponse
{
    public string Answer { get; set; } = "";
    public List<Citation> Citations { get; set; } = new();
    public List<string> ActionHints { get; set; } = new();

    /// <summary>
    /// Trace id = correlationId (also returned in response header x-correlation-id)
    /// </summary>
    public string RequestId { get; set; } = "";

    public EscalationInfo Escalation { get; set; } = new();

    /// <summary>
    /// Optional: helpful for debugging and demos; does not break existing clients.
    /// </summary>
    public ModeInfo Mode { get; set; } = new();
}

/// <summary>
/// Standardized error object (Task10)
/// </summary>
public sealed class ErrorInfo
{
    /// <summary>
    /// e.g. InvalidJson | MissingBody | ValidationError | InternalError
    /// </summary>
    public string Code { get; set; } = "";

    public string Message { get; set; } = "";

    /// <summary>
    /// Optional structured details, e.g. { field = "message" }
    /// </summary>
    public object? Details { get; set; }
}

/// <summary>
/// Error response for POST /api/chat (Task10)
/// </summary>
public sealed class ErrorResponse
{
    /// <summary>
    /// Trace id = correlationId (also returned in response header x-correlation-id)
    /// </summary>
    public string RequestId { get; set; } = "";

    public ErrorInfo Error { get; set; } = new();
}
