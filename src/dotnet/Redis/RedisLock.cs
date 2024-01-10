using StackExchange.Redis;
using ActualLab.Redis;

namespace ActualChat.Redis;

// TODO: prevent refresh/release of not owned lock
public class RedisLock<TContext>(IServiceProvider services, string key) : IAsyncDisposable
{
    private RedisDb? _redisDb;
    private MomentClockSet? _clocks;

    private RedisDb RedisDb => _redisDb ??= services.GetRequiredService<RedisDb<TContext>>().WithKeyPrefix(".Locks.");
    private MomentClockSet Clocks => _clocks ??= services.Clocks();
    private Moment CpuNow => Clocks.CpuClock.Now;

    public ValueTask DisposeAsync()
        => Release().ToVoidValueTask();

    public Task<bool> TryLock(TimeSpan ttl)
        => RedisDb.Database.StringSetAsync(key,
            CpuNow.ToString(),
            ttl,
            When.NotExists,
            CommandFlags.DemandMaster);

    public Task<bool> Release()
        => RedisDb.Database.KeyDeleteAsync(key);

    public Task Refresh(TimeSpan ttl)
        // TODO: check if existing lock is owned by this context
        => RedisDb.Database.KeyExpireAsync(key, ttl, CommandFlags.DemandMaster);

    // !!! It does not guarantee precedence between replicas !!!
    public async Task Wait(TimeSpan ttl, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested) {
            if (await TryLock(ttl).ConfigureAwait(false))
                return;

            var remaining = await RedisDb.Database.KeyTimeToLiveAsync(key).ConfigureAwait(false);
            if (remaining == null)
                continue;

            await Clocks.CpuClock.Delay(remaining.Value, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task<bool> IsStillLocked()
        => RedisDb.Database.KeyExistsAsync(key);

    public async Task Keep(TimeSpan ttl, TimeSpan refreshInterval, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested) {
            await Task.Delay(refreshInterval, cancellationToken).ConfigureAwait(false);
            await Refresh(ttl).ConfigureAwait(false);
        }
    }
}
