namespace FortiAnswer.Orchestrator.Services;

public sealed class PromptBuilder
{
    // Wrapper to match ChatFunction.cs call-site
    public string BuildPrompt(
        string userMessage,
        string requestType,
        string userRole,
        string? userGroup,
        string? conversationId,
        string internalContext,
        string? webContext)
        => Build(userMessage, requestType, userRole, userGroup, conversationId, internalContext, webContext);

    public string Build(
        string userMessage,
        string requestType,
        string userRole,
        string? userGroup,
        string? conversationId,
        string internalContext,
        string? webContext)
    {
        // NOTE: ChatFunction currently passes the effective boundary (Public/Internal/Confidential/Restricted)
        // in the requestType field.

        return $"""
You are FortiAnswer, an enterprise support assistant.

=== HARD RULES (NON-NEGOTIABLE) ===
1) Use ONLY the evidence provided in "Internal evidence" and "Web evidence".
2) If evidence is missing/unclear, say "Not confirmed by sources" and ask a clarifying question.
3) Do NOT invent details, dates, version numbers, or steps.
4) Do NOT paste raw URLs in the answer. Sources must come from the citations list produced by the system.
5) Do NOT claim actions were performed (e.g., "I escalated" / "after escalation" / "we have escalated").
   You MAY mention escalation only as a conditional next step ("If confirmed, escalate to Tier-2") AND only if supported by evidence.
6) Keep the answer concise and actionable.

=== USER METADATA ===
- boundary: {requestType}
- userRole: {userRole}
- userGroup: {userGroup ?? ""}
- conversationId: {conversationId ?? ""}

=== INTERNAL EVIDENCE ===
{internalContext}

=== WEB EVIDENCE (OPTIONAL) ===
{(string.IsNullOrWhiteSpace(webContext) ? "[none]" : webContext)}

=== USER QUESTION ===
{userMessage}
""";
    }
}
