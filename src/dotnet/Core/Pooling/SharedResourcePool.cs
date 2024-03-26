namespace ActualChat.Pooling;

public partial class SharedResourcePool<TKey, TResource>(
    Func<TKey, CancellationToken, Task<TResource>> resourceFactory,
    Func<TKey, TResource, ValueTask>? resourceDisposer = null) : IAsyncDisposable
    where TKey : notnull
    where TResource : class
{
    private readonly ConcurrentDictionary<TKey, Lease> _leases = new ();
    private volatile int _isDisposed;

    private Func<TKey, CancellationToken, Task<TResource>> ResourceFactory { get; } = resourceFactory;
    private Func<TKey, TResource, ValueTask> ResourceDisposer { get; } = resourceDisposer ?? DefaultResourceDisposer;

    public TimeSpan ResourceDisposeDelay { get; init; } = TimeSpan.FromSeconds(10);
    public bool IsDisposed => _isDisposed != 0;
    public ILogger Log { get; init; } = NullLogger.Instance;

    public async ValueTask<Lease> Rent(TKey key, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        while (true) {
            var lease = _leases.GetOrAdd(key, static (key1, self) => new Lease(self, key1), this);
            lease.Initialize(cancellationToken);
            var endRentTask = await lease.BeginRent(cancellationToken).ConfigureAwait(false);
            if (endRentTask == null)
                return lease;

            await endRentTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _isDisposed, 1, 0) != 0)
            return;

        while (!_leases.IsEmpty)
            try {
                var keys = _leases.Keys.ToList();
                foreach (var key in keys) {
                    if (!_leases.TryRemove(key, out var lease))
                        continue;

                    await ResourceDisposer.Invoke(key, lease.Resource).ConfigureAwait(false);
                }
            }
            catch (Exception e) {
                var log = Log == NullLogger.Instance
                    ? DefaultLog
                    : Log;
                log.LogError(e, "Error while disposing {Type}", GetType().GetName());
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
