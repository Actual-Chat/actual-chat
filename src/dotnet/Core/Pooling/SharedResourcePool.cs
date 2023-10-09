namespace ActualChat.Pooling;

public partial class SharedResourcePool<TKey, TResource>(
    Func<TKey, CancellationToken, Task<TResource>> resourceFactory,
    Func<TKey, TResource, ValueTask>? resourceDisposer = null)
    where TKey : notnull
    where TResource : class
{
    private readonly ConcurrentDictionary<TKey, Lease> _leases = new ();
    private Func<TKey, CancellationToken, Task<TResource>> ResourceFactory { get; } = resourceFactory;
    private Func<TKey, TResource, ValueTask> ResourceDisposer { get; } = resourceDisposer ?? DefaultResourceDisposer;

    public TimeSpan ResourceDisposeDelay { get; init; } = TimeSpan.FromSeconds(10);
    public ILogger Log { get; init; } = NullLogger.Instance;

    public async ValueTask<Lease> Rent(TKey key, CancellationToken cancellationToken = default)
    {
        while (true) {
            var lease = _leases.GetOrAdd(key, static (key1, state) => {
                var (self, cancellationToken1) = state;
                return new Lease(self, key1, cancellationToken1);
            }, (this, cancellationToken));
            var endRentTask = await lease.BeginRent(cancellationToken).ConfigureAwait(false);
            if (endRentTask == null)
                return lease;

            await endRentTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async ValueTask DefaultResourceDisposer(TKey key, TResource resource)
    {
        if (resource is IAsyncDisposable ad)
            await ad.DisposeAsync().ConfigureAwait(false);
        else if (resource is IDisposable d)
            d.Dispose();
    }
}
