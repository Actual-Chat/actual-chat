namespace ActualChat.Pooling;

public partial class SharedResourcePool<TKey, TResource>
    where TResource : class
    where TKey : notnull
{
    // ReSharper disable once StaticMemberInGenericType
    protected static TimeSpan DefaultResourceDisposeDelay { get; } = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<TKey, Lease> _leases = new ();
    private Func<TKey, Task<TResource>> ResourceFactory { get; }
    private TimeSpan ResourceDisposeDelay { get; }

    public SharedResourcePool(Func<TKey, Task<TResource>> resourceFactory, TimeSpan? resourceDisposeDelay = null)
    {
        ResourceFactory = resourceFactory;
        ResourceDisposeDelay = resourceDisposeDelay ?? DefaultResourceDisposeDelay;
    }

    public async ValueTask<Lease> Rent(TKey key, CancellationToken cancellationToken = default)
    {
        var spinWait = new SpinWait();
        while (true) {
            var lease = _leases.GetOrAdd(key, static (key1, self) => new Lease(self, key1), this);
            if (await lease.BeginRent(cancellationToken).ConfigureAwait(false))
                return lease;
            spinWait.SpinOnce();
        }
    }
}
