using System.Net;
using System.Text.Json;
using System.Web;
using FortiAnswer.Orchestrator.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace FortiAnswer.Orchestrator.Functions;

/// <summary>
/// GET /api/conversations?username={username} — list all conversation log entries for a user.
/// </summary>
public sealed class ConversationListFunction
{
    private readonly TableStorageService _tables;

    private static readonly JsonSerializerOptions JsonOut =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public ConversationListFunction(TableStorageService tables)
    {
        _tables = tables;
    }

    [Function("Conversation_List")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "conversations")] HttpRequestData req)
    {
        var qs = HttpUtility.ParseQueryString(req.Url.Query);
        var username = qs["username"]?.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(username))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            bad.Headers.Add("Content-Type", "application/json");
            await bad.WriteStringAsync("{\"error\":{\"code\":\"ValidationError\",\"message\":\"Query param 'username' is required.\"}}");
            return bad;
        }

        var entries = await _tables.GetConversationsByUsernameAsync(username);

        var ok = req.CreateResponse(HttpStatusCode.OK);
        ok.Headers.Add("Content-Type", "application/json");
        await ok.WriteStringAsync(JsonSerializer.Serialize(entries, JsonOut));
        return ok;
    }
}
