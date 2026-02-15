namespace FortiAnswer.Orchestrator.Services;

public sealed class PromptBuilder
{
    public string Build(
        string userMessage,
        string requestType,
        string userRole,
        string? userGroup,
        string? conversationId,
        string internalContext,
        string? webContext)
    {
        // For Step 1 we won’t call Groq yet, but we prepare a final prompt format.
        return $"""
You are FortiAnswer, an enterprise support assistant.

Rules:
- Use ONLY the evidence provided below. If evidence is insufficient, say so and ask a clarifying question.
- Be concise and actionable.

User metadata:
- requestType: {requestType}
- userRole: {userRole}
- userGroup: {userGroup ?? ""}
- conversationId: {conversationId ?? ""}

Internal evidence:
{internalContext}

Web evidence (optional):
{(string.IsNullOrWhiteSpace(webContext) ? "[none]" : webContext)}

User question:
{userMessage}
""";
    }
}
