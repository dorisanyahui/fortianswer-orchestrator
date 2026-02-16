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
