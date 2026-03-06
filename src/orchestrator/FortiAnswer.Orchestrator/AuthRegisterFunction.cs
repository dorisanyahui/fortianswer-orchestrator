using System.Net;
using System.Text.Json;
using Azure.Data.Tables;
using BCrypt.Net;
using FortiAnswer.Orchestrator.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace FortiAnswer.Orchestrator.Functions;

public sealed class AuthRegisterFunction
{
    private readonly UsersTableService _users;

    public AuthRegisterFunction(UsersTableService users)
    {
        _users = users;
    }

    private static readonly JsonSerializerOptions JsonIn = new() { PropertyNameCaseInsensitive = true };

    private record RegisterReq(
        string Username,
        string Password,
        string? Email,
        string? Company,
        string? Telephone,
        string? Role);

    [Function("Auth_Register")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/register")] HttpRequestData req)
    {
        var body = await new StreamReader(req.Body).ReadToEndAsync();
        var input = JsonSerializer.Deserialize<RegisterReq>(body, JsonIn);

        if (input is null || string.IsNullOrWhiteSpace(input.Username) || string.IsNullOrWhiteSpace(input.Password))
            return req.CreateResponse(HttpStatusCode.BadRequest);

        var username = input.Username.Trim().ToLowerInvariant();

        // basic password policy
        if (input.Password.Length < 10)
        {
            var r = req.CreateResponse(HttpStatusCode.BadRequest);
            await r.WriteStringAsync("Password must be at least 10 characters.");
            return r;
        }

        // check if exists
        var exists = await _users.GetByUsernameIfExistsAsync(username);
        if (exists is not null)
        {
            var r = req.CreateResponse(HttpStatusCode.Conflict);
            await r.WriteStringAsync("Username already exists.");
            return r;
        }

        // BCrypt hash (includes salt)
        var hash = BCrypt.Net.BCrypt.HashPassword(input.Password, workFactor: 12);

        var entity = new TableEntity("user", username)
        {
            ["PasswordHash"] = hash,
            ["Role"] = string.IsNullOrWhiteSpace(input.Role) ? "Customer" : input.Role,
            ["Email"] = input.Email ?? "",
            ["Company"] = input.Company ?? "",
            ["Telephone"] = input.Telephone ?? "",
            ["CreatedUtc"] = DateTimeOffset.UtcNow.ToString("o"),
            ["IsDisabled"] = false
        };

        await _users.AddAsync(entity);

        var ok = req.CreateResponse(HttpStatusCode.Created);
        await ok.WriteStringAsync("User created.");
        return ok;
    }
}