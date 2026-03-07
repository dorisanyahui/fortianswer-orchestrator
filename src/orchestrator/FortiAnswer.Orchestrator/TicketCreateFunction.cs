using System.Net;
using System.Text.Json;
using FortiAnswer.Orchestrator.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace FortiAnswer.Orchestrator.Functions;

/// <summary>
/// POST /api/tickets — manually create a ticket.
/// Requires a valid, non-disabled username from the Users table.
/// TODO: upgrade to JWT validation once auth tokens are implemented.
/// </summary>
public sealed class TicketCreateFunction
{
    private readonly TicketsTableService _tickets;
    private readonly UsersTableService _users;

    private static readonly JsonSerializerOptions JsonIn  = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions JsonOut = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public TicketCreateFunction(TicketsTableService tickets, UsersTableService users)
    {
        _tickets = tickets;
        _users   = users;
    }

    private record CreateTicketReq(
        string  Username,
        string  Summary,
        string? IssueType,
        string? DataBoundary,
        string? ConversationId);

    [Function("Ticket_Create")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "tickets")] HttpRequestData req)
    {
        var body = await new StreamReader(req.Body).ReadToEndAsync();

        CreateTicketReq? input;
        try { input = JsonSerializer.Deserialize<CreateTicketReq>(body, JsonIn); }
        catch { return await Error(req, HttpStatusCode.BadRequest, "InvalidJson", "Invalid JSON body."); }

        // Validate required fields
        if (input is null || string.IsNullOrWhiteSpace(input.Username))
            return await Error(req, HttpStatusCode.BadRequest, "ValidationError", "Field 'username' is required.");

        if (string.IsNullOrWhiteSpace(input.Summary))
            return await Error(req, HttpStatusCode.BadRequest, "ValidationError", "Field 'summary' is required.");

        // Verify user exists and is not disabled
        var username = input.Username.Trim().ToLowerInvariant();
        var userEntity = await _users.GetByUsernameIfExistsAsync(username);

        if (userEntity is null)
            return await Error(req, HttpStatusCode.Unauthorized, "Unauthorized", "Unknown user.");

        if (userEntity.TryGetValue("IsDisabled", out var disabled) && disabled is bool b && b)
            return await Error(req, HttpStatusCode.Unauthorized, "Unauthorized", "User account is disabled.");

        // Create ticket
        var issueType    = NormalizeIssueType(input.IssueType);
        var dataBoundary = NormalizeBoundary(input.DataBoundary);

        var ticketId = await _tickets.CreateAsync(
            conversationId:   input.ConversationId?.Trim(),
            createdByUser:    username,
            issueType:        issueType,
            dataBoundary:     dataBoundary,
            summary:          input.Summary.Trim(),
            escalationReason: "Manual ticket created by user.",
            source:           "manual");

        var resp = req.CreateResponse(HttpStatusCode.Created);
        resp.Headers.Add("Content-Type", "application/json");

        await resp.WriteStringAsync(JsonSerializer.Serialize(new
        {
            ticketId,
            status   = "Open",
            priority = TicketsTableService.DerivePriority(issueType),
            issueType,
            dataBoundary,
            createdByUser  = username,
            conversationId = input.ConversationId?.Trim(),
            createdUtc     = DateTimeOffset.UtcNow.ToString("o")
        }, JsonOut));

        return resp;
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static string NormalizeIssueType(string? t) =>
        (t ?? "").Trim().ToLowerInvariant() switch
        {
            "vpn"              => "VPN",
            "mfa"              => "MFA",
            "passwordreset"
            or "password_reset" => "PasswordReset",
            "phishing"         => "Phishing",
            "suspiciouslogin"  => "SuspiciousLogin",
            "endpointalert"    => "EndpointAlert",
            "accountlockout"
            or "lockout"       => "AccountLockout",
            "severity"         => "Severity",
            _                  => "General"
        };

    private static string NormalizeBoundary(string? b) =>
        (b ?? "").Trim().ToLowerInvariant() switch
        {
            "internal"     => "Internal",
            "confidential" => "Confidential",
            "restricted"   => "Restricted",
            _              => "Public"
        };

    private static async Task<HttpResponseData> Error(
        HttpRequestData req,
        HttpStatusCode status,
        string code,
        string message)
    {
        var resp = req.CreateResponse(status);
        resp.Headers.Add("Content-Type", "application/json");
        await resp.WriteStringAsync(JsonSerializer.Serialize(
            new { error = new { code, message } },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        return resp;
    }
}
