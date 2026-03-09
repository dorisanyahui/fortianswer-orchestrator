using System.Net;
using System.Text.Json;
using System.Web;
using FortiAnswer.Orchestrator.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace FortiAnswer.Orchestrator.Functions;

/// <summary>
/// GET /api/tickets?username={username} — list all tickets for a user.
/// </summary>
public sealed class TicketListFunction
{
    private readonly TicketsTableService _tickets;

    private static readonly JsonSerializerOptions JsonOut =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public TicketListFunction(TicketsTableService tickets)
    {
        _tickets = tickets;
    }

    [Function("Ticket_List")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "tickets")] HttpRequestData req)
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

        var tickets = await _tickets.GetByUsernameAsync(username);

        var ok = req.CreateResponse(HttpStatusCode.OK);
        ok.Headers.Add("Content-Type", "application/json");
        await ok.WriteStringAsync(JsonSerializer.Serialize(tickets, JsonOut));
        return ok;
    }
}
