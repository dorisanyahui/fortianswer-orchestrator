using System.Net;
using System.Text.Json;
using FortiAnswer.Orchestrator.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace FortiAnswer.Orchestrator.Functions;

/// <summary>
/// GET /api/tickets/{id} — retrieve a single ticket by ID.
/// </summary>
public sealed class TicketGetFunction
{
    private readonly TicketsTableService _tickets;

    private static readonly JsonSerializerOptions JsonOut =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public TicketGetFunction(TicketsTableService tickets)
    {
        _tickets = tickets;
    }

    [Function("Ticket_Get")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "tickets/{id}")] HttpRequestData req,
        string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            bad.Headers.Add("Content-Type", "application/json");
            await bad.WriteStringAsync("{\"error\":{\"code\":\"ValidationError\",\"message\":\"Ticket id is required.\"}}");
            return bad;
        }

        var ticket = await _tickets.GetByIdAsync(id.Trim());

        if (ticket is null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            notFound.Headers.Add("Content-Type", "application/json");
            await notFound.WriteStringAsync("{\"error\":{\"code\":\"NotFound\",\"message\":\"Ticket not found.\"}}");
            return notFound;
        }

        var ok = req.CreateResponse(HttpStatusCode.OK);
        ok.Headers.Add("Content-Type", "application/json");
        await ok.WriteStringAsync(JsonSerializer.Serialize(ticket, JsonOut));
        return ok;
    }
}
