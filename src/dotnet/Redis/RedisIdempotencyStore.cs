using System.Text;
using ActualChat.Commands;
using ActualLab.Redis;
using StackExchange.Redis;

namespace ActualChat.Redis;

/// <summary>
/// <see cref="IIdempotencyStore"/> over the shared <see cref="RedisDb{TContext}"/>: a claim is a single
/// <c>SET NX</c> marker <c>[tag][owner]</c> with a short TTL, completion overwrites it with <c>[tag][result]</c>
/// and a longer TTL. Reclaiming a dead owner's claim is a compare-and-set Lua script.
/// </summary>
public sealed class RedisIdempotencyStore(IServiceProvider services) : IIdempotencyStore
{
    private const byte InProgressTag = 0x00;
    private const byte CompletedTag = 0x01;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(25);

    // KEYS[1]=key, ARGV[1]=expectedOwner, ARGV[2]=new marker, ARGV[3]=ttlMs.
    // Returns {0}=gone, {1}=reclaimed, {2, result}=completed, {3}=owner changed.
    private const string ReclaimScript = """
        local v = redis.call('GET', KEYS[1])
        if not v then return {0} end
        if string.byte(v, 1) == 1 then return {2, string.sub(v, 2)} end
        if string.sub(v, 2) == ARGV[1] then
            redis.call('SET', KEYS[1], ARGV[2], 'PX', ARGV[3])
            return {1}
        end
        return {3}
        """;

    private RedisDb RedisDb { get; } = services
        .GetRequiredService<RedisDb<InfrastructureDbContext>>()
        .WithKeyPrefix("ApiCmdDedup");

    public async Task<IdempotencyEntry> ClaimOrGet(
        string key, string owner, TimeSpan ttl, CancellationToken cancellationToken)
    {
        // RedisDb.Database already applies the key prefix, so every call below passes the bare key
        var db = await RedisDb.Database.Get(cancellationToken).ConfigureAwait(false);
        var marker = Marker(InProgressTag, Encoding.UTF8.GetBytes(owner));
        for (var i = 0; i < 4; i++) {
            var claimed = await db.StringSetAsync(key, marker, ttl, When.NotExists).ConfigureAwait(false);
            if (claimed)
                return new IdempotencyEntry(IdempotencyState.New, Owner: owner);

            var raw = await db.StringGetAsync(key).ConfigureAwait(false);
            if (raw.IsNullOrEmpty)
                continue; // Marker expired between SET NX and GET — retry the claim.

            var bytes = (byte[])raw!;
            return bytes[0] == CompletedTag
                ? new IdempotencyEntry(IdempotencyState.Completed, bytes.AsMemory(1))
                : new IdempotencyEntry(IdempotencyState.InProgress, Owner: Encoding.UTF8.GetString(bytes, 1, bytes.Length - 1));
        }
        return new IdempotencyEntry(IdempotencyState.InProgress);
    }

    public async Task Complete(
        string key, ReadOnlyMemory<byte> resultMessage, TimeSpan ttl, CancellationToken cancellationToken)
    {
        var db = await RedisDb.Database.Get(cancellationToken).ConfigureAwait(false);
        var value = Marker(CompletedTag, resultMessage.Span);
        await db.StringSetAsync(key, value, ttl, When.Always).ConfigureAwait(false);
    }

    public async Task<ReadOnlyMemory<byte>?> WaitForResult(
        string key, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var db = await RedisDb.Database.Get(cancellationToken).ConfigureAwait(false);
        var attempts = (int)(timeout.TotalMilliseconds / PollInterval.TotalMilliseconds) + 1;
        for (var i = 0; i < attempts; i++) {
            var raw = await db.StringGetAsync(key).ConfigureAwait(false);
            if (raw.IsNullOrEmpty)
                return null; // Marker gone without a result — owner died; caller re-claims.

            var bytes = (byte[])raw!;
            if (bytes[0] == CompletedTag)
                return bytes.AsMemory(1);

            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }
        return null;
    }

    public async Task<IdempotencyEntry?> TryReclaim(
        string key, string expectedOwner, string newOwner, TimeSpan ttl, CancellationToken cancellationToken)
    {
        var db = await RedisDb.Database.Get(cancellationToken).ConfigureAwait(false);
        var newMarker = Marker(InProgressTag, Encoding.UTF8.GetBytes(newOwner));
        var res = await db.ScriptEvaluateAsync(
            ReclaimScript,
            [key],
            [expectedOwner, newMarker, (long)ttl.TotalMilliseconds]).ConfigureAwait(false);
        var arr = (RedisResult[])res!;
        return (int)arr[0] switch {
            1 => new IdempotencyEntry(IdempotencyState.New, Owner: newOwner), // reclaimed
            2 => new IdempotencyEntry(IdempotencyState.Completed, ((byte[])arr[1]!).AsMemory()),
            _ => null, // Gone or owner changed — caller re-claims.
        };
    }

    public async Task Release(string key, CancellationToken cancellationToken)
    {
        var db = await RedisDb.Database.Get(cancellationToken).ConfigureAwait(false);
        await db.KeyDeleteAsync(key).ConfigureAwait(false);
    }

    // Private methods

    private static byte[] Marker(byte tag, ReadOnlySpan<byte> payload)
    {
        var value = new byte[payload.Length + 1];
        value[0] = tag;
        payload.CopyTo(value.AsSpan(1));
        return value;
    }
}
