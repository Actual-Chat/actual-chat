namespace ActualChat.Pooling;

/// <summary>
/// A pool of shared resources keyed by an identifier, with reference counting.
/// </summary>
public partial class SharedResourcePool<TKey, TResource>(
    Func<TKey, CancellationToken, Task<TResource>> resourceFactory,
    Func<TKey, TResource, ValueTask>? resourceDisposer = null) : IAsyncDisposable
    where TKey : notnull
    where TResource : class
{
    private readonly ConcurrentDictionary<TKey, Lease> _leases = new ();
    private readonly CancellationTokenSource _disposeTokenSource = new();
    private volatile int _isDisposed;
    private ILogger? _log;

    private Func<TKey, CancellationToken, Task<TResource>> ResourceFactory { get; } = resourceFactory;
    private Func<TKey, TResource, ValueTask> ResourceDisposer { get; } = resourceDisposer ?? DefaultResourceDisposer;

    public TimeSpan ResourceDisposeDelay { get; init; } = TimeSpan.FromSeconds(10);
    public CancellationToken DisposeToken => _disposeTokenSource.Token;
    public bool IsDisposed => _isDisposed != 0;

    public ILogger Log {
        get => _log ??= StaticLog.For(GetType());
        init => _log = value;
    }

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

        // Cancel in-flight resource factories so their leases self-clean via Lease.BeginRent's
        // OperationCanceledException catch path (which removes the lease from _leases).
        await _disposeTokenSource.CancelAsync().ConfigureAwait(false);

        // Multi-round disposal: dispose what's ready now; leave in-flight leases in _leases
        // and revisit them in the next round. Keep going until everything is gone or the
        // overall DisposeTimeout is hit.
        var deadline = CpuTimestamp.Now + CoreConstants.DisposeTimeout;
        var roundDelay = TimeSpan.FromMilliseconds(50);
        while (!_leases.IsEmpty) {
            if (CpuTimestamp.Now >= deadline) {
                Log.LogWarning(
                    "{Type}: dispose timed out, {LeftoverCount} resource(s) won't be disposed",
                    GetType().GetName(), _leases.Count);
                break;
            }

            var disposedCount = 0;
            var pendingCount = 0;
            var keys = _leases.Keys.ToList();
            foreach (var key in keys) {
                if (!_leases.TryGetValue(key, out var lease))
                    continue; // Already self-cleaned (e.g. by BeginRent's cancellation catch)
                if (!lease.TryTakeCompletedResourceForPoolDispose(out var resource)) {
                    // Resource factory still in flight — defer to the next round.
                    pendingCount++;
                    continue;
                }
                if (!_leases.TryRemove(new KeyValuePair<TKey, Lease>(key, lease)))
                    continue;
                try {
                    await ResourceDisposer.Invoke(key, resource).ConfigureAwait(false);
                    disposedCount++;
                }
                catch (Exception e) {
                    Log.LogError(e,
                        "{Type}: failed to dispose resource for key {Key}",
                        GetType().GetName(), key);
                }
            }

            if (_leases.IsEmpty)
                break;

            // Nothing was ready this round — wait briefly to let factories react to the
            // cancellation before scanning again. We use Task.Delay with no token because
            // the only thing we're waiting on is the round delay itself.
            if (disposedCount == 0 && pendingCount > 0)
                await Task.Delay(roundDelay).ConfigureAwait(false);
        }

        _disposeTokenSource.Dispose();
    }

    private static async ValueTask DefaultResourceDisposer(TKey key, TResource resource)
    {
        if (resource is IAsyncDisposable ad)
            await ad.DisposeAsync().ConfigureAwait(false);
        else if (resource is IDisposable d)
            d.Dispose();
    }
}
