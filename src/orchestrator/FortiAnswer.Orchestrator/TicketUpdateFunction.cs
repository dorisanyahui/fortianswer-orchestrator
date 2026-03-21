using System.Net;
using System.Text.Json;
using FortiAnswer.Orchestrator.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace FortiAnswer.Orchestrator.Functions;

/// <summary>
/// PATCH /api/tickets/{id} — update mutable ticket fields (status, assignedTo, priority).
/// Intended for Agent and Admin roles only.
///
/// Request body (all fields optional — only non-null values are applied):
/// {
///   "status":     "Open" | "InProgress" | "Closed",
///   "assignedTo": "agent-username",
///   "priority":   "P1" | "P2" | "P3" | "P4"
/// }
/// </summary>
public sealed class TicketUpdateFunction
{
    private static readonly HashSet<string> AllowedRoles =
        new(StringComparer.OrdinalIgnoreCase) { "agent", "admin" };

    private static readonly HashSet<string> ValidStatuses =
        new(StringComparer.OrdinalIgnoreCase) { "Open", "InProgress", "Closed" };

    private static readonly HashSet<string> ValidPriorities =
        new(StringComparer.OrdinalIgnoreCase) { "P1", "P2", "P3", "P4" };

    private static readonly JsonSerializerOptions JsonIn =
        new() { PropertyNameCaseInsensitive = true };

    private static readonly JsonSerializerOptions JsonOut =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly TicketsTableService _tickets;

    public TicketUpdateFunction(TicketsTableService tickets)
    {
        _tickets = tickets;
    }

    [Function("Ticket_Update")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "tickets/{id}")] HttpRequestData req,
        string id)
    {
        // --- Role check (passed as query param, same pattern as other endpoints) ---
        var role = System.Web.HttpUtility.ParseQueryString(req.Url.Query)["role"]?.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(role) || !AllowedRoles.Contains(role))
        {
            var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
            forbidden.Headers.Add("Content-Type", "application/json");
            await forbidden.WriteStringAsync(
                "{\"error\":{\"code\":\"Forbidden\",\"message\":\"Only agents and admins can update tickets. Supply role=agent or role=admin.\"}}");
            return forbidden;
        }

        // --- Parse body ---
        TicketUpdateRequest? update;
        try
        {
            update = await JsonSerializer.DeserializeAsync<TicketUpdateRequest>(req.Body, JsonIn);
        }
        catch
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            bad.Headers.Add("Content-Type", "application/json");
            await bad.WriteStringAsync(
                "{\"error\":{\"code\":\"InvalidJson\",\"message\":\"Request body is not valid JSON.\"}}");
            return bad;
        }

        if (update is null)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            bad.Headers.Add("Content-Type", "application/json");
            await bad.WriteStringAsync(
                "{\"error\":{\"code\":\"MissingBody\",\"message\":\"Request body is required.\"}}");
            return bad;
        }

        // --- Validate supplied values ---
        if (update.Status is not null && !ValidStatuses.Contains(update.Status))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            bad.Headers.Add("Content-Type", "application/json");
            await bad.WriteStringAsync(
                "{\"error\":{\"code\":\"ValidationError\",\"message\":\"Invalid status. Allowed: Open, InProgress, Closed.\"}}");
            return bad;
        }

        if (update.Priority is not null && !ValidPriorities.Contains(update.Priority))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            bad.Headers.Add("Content-Type", "application/json");
            await bad.WriteStringAsync(
                "{\"error\":{\"code\":\"ValidationError\",\"message\":\"Invalid priority. Allowed: P1, P2, P3, P4.\"}}");
            return bad;
        }

        // --- Apply update ---
        var found = await _tickets.UpdateAsync(id.Trim(), update);

        if (!found)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            notFound.Headers.Add("Content-Type", "application/json");
            await notFound.WriteStringAsync(
                "{\"error\":{\"code\":\"NotFound\",\"message\":\"Ticket not found.\"}}");
            return notFound;
        }

        // Return the updated ticket so the caller doesn't need a second GET.
        var ticket = await _tickets.GetByIdAsync(id.Trim());
        var ok = req.CreateResponse(HttpStatusCode.OK);
        ok.Headers.Add("Content-Type", "application/json");
        await ok.WriteStringAsync(JsonSerializer.Serialize(ticket, JsonOut));
        return ok;
    }
}
