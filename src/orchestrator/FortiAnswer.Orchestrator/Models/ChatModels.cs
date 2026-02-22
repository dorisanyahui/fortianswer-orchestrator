using System.Collections.Generic;

namespace FortiAnswer.Orchestrator.Models;

/// <summary>
/// Incoming request body for POST /api/chat
/// </summary>
public sealed class ChatRequest
{
    /// <summary>
    /// User natural-language question.
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Sprint1 UI scenario selection (user-facing).
    /// Examples: "Phishing" | "VPN" | "MFA" | "SuspiciousLogin" | "EndpointAlert" | "PasswordReset" | "General"
    /// </summary>
    public string? IssueType { get; set; }

    /// <summary>
    /// Data boundary (security classification / access level).
    /// Server should decide this from user identity/role, not trust the client.
    /// Examples: "Public" | "Internal" | "Confidential" | "Restricted"
    /// </summary>
    public string? DataBoundary { get; set; }

    /// <summary>
    /// Legacy field kept for backward compatibility with older UI clients.
    /// DEPRECATED: treat as DataBoundary if DataBoundary is null.
    /// </summary>
    public string? RequestType { get; set; }

    /// <summary>
    /// User role hint. Keep for demos; do NOT rely on this for real RBAC.
    /// Preferred: derive from auth claims or trusted headers.
    /// </summary>
    public string? UserRole { get; set; }

    /// <summary>
    /// User group hint. Keep for demos; do NOT rely on this for real RBAC.
    /// </summary>
    public string? UserGroup { get; set; }

    /// <summary>
    /// Optional conversation/session id for UI threading.
    /// </summary>
    public string? ConversationId { get; set; }

    /// <summary>
    /// When true, user confirms allowing external web search (Public only).
    /// </summary>
    public bool? ConfirmWebSearch { get; set; }

    /// <summary>
    /// Signed token issued by server when it asks user to confirm web search.
    /// Required when ConfirmWebSearch=true.
    /// </summary>
    public string? WebSearchToken { get; set; }
}

/// <summary>
/// Evidence snippet returned from Retrieval or Web Search.
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
    public string Llm { get; set; } = "stub";       // stub | groq | azureopenai | ...
}

/// <summary>
/// Success response for POST /api/chat
/// </summary>
public sealed class ChatResponse
{
    public string Answer { get; set; } = "";

    public List<Citation> Citations { get; set; } = new();

    /// <summary>
    /// Debug breadcrumbs for demo/diagnostics (e.g., used:internal_search, used:web_search, boundary=Public).
    /// </summary>
    public List<string> ActionHints { get; set; } = new();

    /// <summary>
    /// True when the server requires user confirmation before running web search.
    /// </summary>
    public bool NeedsWebConfirmation { get; set; }

    /// <summary>
    /// Server-issued confirmation token. Client must resend it with ConfirmWebSearch=true.
    /// </summary>
    public string? WebSearchToken { get; set; }

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
/// Standardized error object
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
/// Error response for POST /api/chat
/// </summary>
public sealed class ErrorResponse
{
    /// <summary>
    /// Trace id = correlationId (also returned in response header x-correlation-id)
    /// </summary>
    public string RequestId { get; set; } = "";

    public ErrorInfo Error { get; set; } = new();
}