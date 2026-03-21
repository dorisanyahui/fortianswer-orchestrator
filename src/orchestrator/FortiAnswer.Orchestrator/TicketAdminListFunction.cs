using System.Net;
using System.Text.Json;
using System.Web;
using FortiAnswer.Orchestrator.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace FortiAnswer.Orchestrator.Functions;

/// <summary>
/// GET /api/admin/tickets — full ticket overview for Agent and Admin roles.
/// Both roles see all tickets; use query params to filter.
///
/// Query params (all optional):
///   role       — must be "agent" or "admin" (required for access)
///   status     — Open | InProgress | Closed
///   priority   — P1 | P2 | P3 | P4
///   issueType  — Phishing | VPN | MFA | etc.
///   assignedTo — username of assigned agent
///   page       — page number, default 1
///   pageSize   — records per page, default 20, max 100
/// </summary>
public sealed class TicketAdminListFunction
{
    private static readonly HashSet<string> AllowedRoles =
        new(StringComparer.OrdinalIgnoreCase) { "agent", "admin" };

    private static readonly JsonSerializerOptions JsonOut =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly TicketsTableService _tickets;

    public TicketAdminListFunction(TicketsTableService tickets)
    {
        _tickets = tickets;
    }

    [Function("TicketAdmin_List")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "tickets/all")] HttpRequestData req)
    {
        var qs   = HttpUtility.ParseQueryString(req.Url.Query);
        var role = qs["role"]?.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(role) || !AllowedRoles.Contains(role))
        {
            var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
            forbidden.Headers.Add("Content-Type", "application/json");
            await forbidden.WriteStringAsync(
                "{\"error\":{\"code\":\"Forbidden\",\"message\":\"Only agents and admins can access this endpoint. Supply role=agent or role=admin.\"}}");
            return forbidden;
        }

        var allTickets = await _tickets.GetAllAsync(
            status:     qs["status"]?.Trim(),
            priority:   qs["priority"]?.Trim(),
            issueType:  qs["issueType"]?.Trim(),
            assignedTo: qs["assignedTo"]?.Trim());

        // Pagination
        var page     = int.TryParse(qs["page"],     out var p)  && p  > 0 ? p  : 1;
        var pageSize = int.TryParse(qs["pageSize"],  out var ps) && ps > 0 ? Math.Min(ps, 100) : 20;
        var total      = allTickets.Count;
        var totalPages = (int)Math.Ceiling(total / (double)pageSize);
        var tickets    = allTickets.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        var ok = req.CreateResponse(HttpStatusCode.OK);
        ok.Headers.Add("Content-Type", "application/json");
        await ok.WriteStringAsync(JsonSerializer.Serialize(
            new { total, page, pageSize, totalPages, tickets }, JsonOut));
        return ok;
    }
}
