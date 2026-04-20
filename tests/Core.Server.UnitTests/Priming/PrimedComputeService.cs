using ActualLab.Locking;

namespace ActualChat.Core.Server.UnitTests.Priming;

public class PrimedComputeService : IComputeService
{
    private readonly ConcurrentDictionary<string, int> _storage = new(StringComparer.Ordinal);

    public LockingComputeMethodPrimer<string, int> Primer { get; }
    public int ComputeCount;
    public int StorageReadCount;

    public PrimedComputeService()
        => Primer = new LockingComputeMethodPrimer<string, int>(Get, LockReentryMode.Unchecked);

    [ComputeMethod]
    public virtual Task<int> Get(string key, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref ComputeCount);
        if (Primer.TryUsePrimed(key, out var primed))
            return Task.FromResult(primed);

        Interlocked.Increment(ref StorageReadCount);
        return Task.FromResult(_storage.TryGetValue(key, out var v) ? v : 0);
    }

    public async Task Set(string key, int value, CancellationToken cancellationToken = default)
    {
        using var r = await Primer.LockAndPrepare(key, cancellationToken).ConfigureAwait(false);
        _storage[key] = value;
        await r.Prime(value, cancellationToken).ConfigureAwait(false);
    }

    public Task SetRaw(string key, int value, CancellationToken cancellationToken = default)
    {
        _storage[key] = value;
        using (Invalidation.Begin())
            _ = Get(key, default);
        return Task.CompletedTask;
    }
}
