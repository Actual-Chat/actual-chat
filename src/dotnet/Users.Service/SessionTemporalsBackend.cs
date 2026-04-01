using ActualChat.Users.Db;
using ActualLab.Redis;

namespace ActualChat.Users;

public class SessionTemporalsBackend(IServiceProvider services) : ISessionTemporalsBackend
{
    public const int MaxKeyLength = 256;
    public const int MaxValueLength = 1024;
    public const int MaxEntriesPerSession = 100;

    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    private IServiceProvider Services { get; } = services;
    private RedisDb RedisDb { get; } = services.GetRequiredService<RedisDb<UsersDbContext>>().WithKeyPrefix("Tmp");
    private ILogger Log => field ??= Services.LogFor(GetType());

    // [ComputeMethod]
    public virtual async Task<string?> Get(Session session, string key, CancellationToken cancellationToken)
    {
        var db = await RedisDb.Database.Get(cancellationToken).ConfigureAwait(false);
        var redisKey = RedisDb.FullKey(session.Id);
        var redisValue = await db.HashGetAsync(redisKey, key).ConfigureAwait(false);
        var value = redisValue.IsNullOrEmpty ? null : redisValue.ToString();
        // Log.LogWarning("Get: {Session}/{Key} = {Value}", session, key, value ?? "null");
        return value;
    }

    // [CommandHandler]
    public virtual async Task OnSet(SessionTemporalsBackend_Set command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // Self-invalidating command

        if (command.Key.Length > MaxKeyLength)
            throw new ArgumentOutOfRangeException(nameof(command),
                $"Key length must be at most {MaxKeyLength} characters.");

        var db = await RedisDb.Database.Get(cancellationToken).ConfigureAwait(false);
        var redisKey = RedisDb.FullKey(command.Session.Id);
        // Log.LogWarning("OnSet: {Session}/{Key} = {Value}", command.Session, command.Key, command.Value ?? "null");

        if (command.Value is { } value) {
            if (value.Length > MaxValueLength)
                throw new ArgumentOutOfRangeException(nameof(command),
                    $"Value length must be at most {MaxValueLength} characters.");

            var entryCount = await db.HashLengthAsync(redisKey).ConfigureAwait(false);
            var exists = await db.HashExistsAsync(redisKey, command.Key).ConfigureAwait(false);
            if (!exists && entryCount >= MaxEntriesPerSession)
                throw new InvalidOperationException(
                    $"Session temporal storage limit exceeded ({MaxEntriesPerSession} entries).");

            await db.HashSetAsync(redisKey, command.Key, value).ConfigureAwait(false);
        }
        else
            await db.HashDeleteAsync(redisKey, command.Key).ConfigureAwait(false);

        using (Invalidation.Begin())
            _ = Get(command.Session, command.Key, default);

        await db.KeyExpireAsync(redisKey, Ttl).ConfigureAwait(false);
    }
}
