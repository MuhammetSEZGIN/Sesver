using StackExchange.Redis;

namespace ApiGateway.Services;

public sealed class RedisGatewayTokenVersionStore : IGatewayTokenVersionStore
{
    private const string KeyPrefix = "auth:token-version:";
    private readonly IDatabase _database;

    public RedisGatewayTokenVersionStore(IConnectionMultiplexer connection)
    {
        _database = connection.GetDatabase();
    }

    public async Task<int?> GetAsync(string userId)
    {
        var value = await _database.StringGetAsync($"{KeyPrefix}{userId}");
        return value.HasValue && int.TryParse(value.ToString(), out var version)
            ? version
            : null;
    }
}
