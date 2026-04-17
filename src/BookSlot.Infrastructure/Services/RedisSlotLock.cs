using StackExchange.Redis;

namespace BookSlot.Infrastructure.Services;

/// <summary>
/// Redis-backed implementation of <see cref="ISlotLock"/>. Uses <c>SET key value NX EX</c>
/// (set-if-not-exists with expiry) for acquire and a Lua script for safe release.
/// </summary>
internal sealed class RedisSlotLock : ISlotLock
{
    private readonly IDatabase _db;

    /// <summary>Creates a new instance using the given Redis connection.</summary>
    public RedisSlotLock(IConnectionMultiplexer redis) => _db = redis.GetDatabase();

    /// <inheritdoc />
    public async Task<ISlotLockHandle?> TryAcquireAsync(string key, TimeSpan expiry, CancellationToken cancellationToken = default)
    {
        var token = Guid.NewGuid().ToString("N");
        var acquired = await _db.StringSetAsync(key, token, expiry, When.NotExists).ConfigureAwait(false);
        return acquired ? new Handle(_db, key, token) : null;
    }

    // -------------------------------------------------------------------------

    private sealed class Handle : ISlotLockHandle
    {
        // Lua script: only delete the key if its current value equals the supplied token.
        // This prevents a slow caller from releasing a lock re-acquired by someone else.
        private const string ReleaseScript = """
            if redis.call("get", KEYS[1]) == ARGV[1] then
                return redis.call("del", KEYS[1])
            else
                return 0
            end
            """;

        private readonly IDatabase _db;
        private readonly string _key;
        private readonly string _token;

        internal Handle(IDatabase db, string key, string token)
        {
            _db = db;
            _key = key;
            _token = token;
        }

        public async ValueTask DisposeAsync()
        {
            await _db.ScriptEvaluateAsync(
                ReleaseScript,
                keys: [new RedisKey(_key)],
                values: [new RedisValue(_token)]).ConfigureAwait(false);
        }
    }
}
