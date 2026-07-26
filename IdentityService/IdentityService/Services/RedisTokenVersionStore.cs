using IdentityService.Interfaces;
using StackExchange.Redis;

namespace IdentityService.Services;

public sealed class RedisTokenVersionStore : ITokenVersionStore
{
    private const string KeyPrefix = "auth:token-version:";
    private readonly IDatabase _database;
    private readonly ILogger<RedisTokenVersionStore> _logger;

    public RedisTokenVersionStore(
        IConnectionMultiplexer connection,
        ILogger<RedisTokenVersionStore> logger
    )
    {
        _database = connection.GetDatabase();
        _logger = logger;
    }

    public async Task<bool> SetAtLeastAsync(string userId, int tokenVersion)
    {
        const string script = """
            local current = redis.call('GET', KEYS[1])
            local requested = tonumber(ARGV[1])
            if not current or tonumber(current) < requested then
                redis.call('SET', KEYS[1], requested)
            end
            return 1
            """;

        try
        {
            await _database.ScriptEvaluateAsync(
                script,
                new RedisKey[] { GetKey(userId) },
                new RedisValue[] { tokenVersion }
            );
            return true;
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Could not synchronize token version for user {UserId}", userId);
            return false;
        }
    }

    public async Task<int?> IncrementAsync(string userId, int databaseVersion)
    {
        const string script = """
            local current = redis.call('GET', KEYS[1])
            local seed = tonumber(ARGV[1])
            if not current or tonumber(current) < seed then
                redis.call('SET', KEYS[1], seed)
            end
            return redis.call('INCR', KEYS[1])
            """;

        try
        {
            var result = await _database.ScriptEvaluateAsync(
                script,
                new RedisKey[] { GetKey(userId) },
                new RedisValue[] { databaseVersion }
            );
            return (int)(long)result;
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Could not increment token version for user {UserId}", userId);
            return null;
        }
    }

    private static RedisKey GetKey(string userId) => $"{KeyPrefix}{userId}";
}
