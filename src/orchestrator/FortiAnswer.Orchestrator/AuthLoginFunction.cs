using System.Net;
using System.Text.Json;
using FortiAnswer.Orchestrator.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace FortiAnswer.Orchestrator.Functions;

public sealed class AuthLoginFunction
{
    private readonly UsersTableService _users;
    private static readonly JsonSerializerOptions JsonIn = new() { PropertyNameCaseInsensitive = true };

    public AuthLoginFunction(UsersTableService users)
    {
        _users = users;
    }

    private record LoginReq(string Username, string Password);

    [Function("Auth_Login")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/login")] HttpRequestData req)
    {
        var body = await new StreamReader(req.Body).ReadToEndAsync();
        var input = JsonSerializer.Deserialize<LoginReq>(body, JsonIn);

        if (input is null || string.IsNullOrWhiteSpace(input.Username) || string.IsNullOrWhiteSpace(input.Password))
            return req.CreateResponse(HttpStatusCode.BadRequest);

        var username = input.Username.Trim().ToLowerInvariant();

        var entity = await _users.GetByUsernameIfExistsAsync(username);
        if (entity is null)
        {
            var r = req.CreateResponse(HttpStatusCode.Unauthorized);
            await r.WriteStringAsync("Invalid credentials.");
            return r;
        }

        if (entity.TryGetValue("IsDisabled", out var disabledObj) &&
            disabledObj is bool disabled && disabled)
        {
            var r = req.CreateResponse(HttpStatusCode.Unauthorized);
            await r.WriteStringAsync("Invalid credentials.");
            return r;
        }

        var storedHash = entity.TryGetValue("PasswordHash", out var hashObj) ? hashObj?.ToString() : null;
        if (string.IsNullOrWhiteSpace(storedHash))
        {
            var r = req.CreateResponse(HttpStatusCode.Unauthorized);
            await r.WriteStringAsync("Invalid credentials.");
            return r;
        }

        var ok = BCrypt.Net.BCrypt.Verify(input.Password, storedHash);
        if (!ok)
        {
            var r = req.CreateResponse(HttpStatusCode.Unauthorized);
            await r.WriteStringAsync("Invalid credentials.");
            return r;
        }

        // 返回基本信息（后面你可以升级成 JWT）
        var role = entity.TryGetValue("Role", out var roleObj) ? roleObj?.ToString() : "Customer";

        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(new { authenticated = true, username, role });
        return resp;
    }
}