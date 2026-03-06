using Azure;
using Azure.Data.Tables;

namespace FortiAnswer.Orchestrator.Services;

public sealed class UsersTableService
{
    private readonly TableClient _table;

    public UsersTableService()
    {
        var conn = Environment.GetEnvironmentVariable("USERS_TABLE_CONN")
                   ?? throw new InvalidOperationException("Missing USERS_TABLE_CONN");
        var tableName = Environment.GetEnvironmentVariable("USERS_TABLE_NAME") ?? "Users";
        _table = new TableClient(conn, tableName);
        _table.CreateIfNotExists();
    }

    public Task<Response<TableEntity>> GetByUsernameAsync(string username)
        => _table.GetEntityAsync<TableEntity>("user", username.ToLowerInvariant());

    public async Task<TableEntity?> GetByUsernameIfExistsAsync(string username)
    {
        var res = await _table.GetEntityIfExistsAsync<TableEntity>("user", username.ToLowerInvariant());
        return res.HasValue ? res.Value : null;
    }

    public Task AddAsync(TableEntity entity) => _table.AddEntityAsync(entity);

    public Task UpdateAsync(TableEntity entity) => _table.UpsertEntityAsync(entity, TableUpdateMode.Replace);
}