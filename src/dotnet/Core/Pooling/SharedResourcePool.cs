using Microsoft.Extensions.Logging.Abstractions;

namespace ActualChat.Pooling;

public partial class SharedResourcePool<TKey, TResource>
    where TKey : notnull
    where TResource : class
{
    private readonly ConcurrentDictionary<TKey, Lease> _leases = new ();
    private Func<TKey, CancellationToken, Task<TResource>> ResourceFactory { get; }

    public TimeSpan ResourceDisposeDelay { get; init; } = TimeSpan.FromSeconds(10);
    public ILogger Log { get; init; } = NullLogger.Instance;

    public SharedResourcePool(Func<TKey, CancellationToken, Task<TResource>> resourceFactory)
        => ResourceFactory = resourceFactory;

    public async ValueTask<Lease> Rent(TKey key, CancellationToken cancellationToken = default)
    {
        var spinWait = new SpinWait();
        while (true) {
            var lease = _leases.GetOrAdd(key, static (key1, state) => {
                var (self, cancellationToken1) = state;
                return new Lease(self, key1, cancellationToken1);
            }, (this, cancellationToken));
            if (await lease.BeginRent().ConfigureAwait(false))
                return lease;
            spinWait.SpinOnce();
        }
    }
}
