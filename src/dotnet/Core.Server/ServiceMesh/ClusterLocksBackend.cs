using Cysharp.Text;

namespace ActualChat.ServiceMesh;

public abstract class ClusterLocksBackend(IMomentClock? clock = null)
{
    protected readonly string HolderKeyPrefix = Alphabet.AlphaNumeric.Generator8.Next() + "-";
    protected long LastHolderId;

    public ClusterLockOptions DefaultOptions { get; init; } = new(TimeSpan.FromSeconds(10), 0.5f, TimeSpan.FromSeconds(10));
    public required IMomentClock Clock { get; init; } = clock ?? MomentClockSet.Default.SystemClock;

    public virtual async Task<ClusterLockHolder> Acquire(
        Symbol key, string value,
        ClusterLockOptions options,
        CancellationToken cancellationToken = default)
    {
        options = options.WithDefaults(DefaultOptions);
        options.AssertValid();
        var holder = CreateHolder(key, value, options);
        while (true) {
            var expiresAt = await TryAcquire(holder.Key, holder.StoredValue, options.ExpirationPeriod, cancellationToken).ConfigureAwait(false);
            if (!expiresAt.HasValue)
                break;

            using var timeoutCts = new CancellationTokenSource(options.CheckPeriod);
            using var cts = cancellationToken.LinkWith(timeoutCts.Token);
            try {
                await WhenChanged(key, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
                if (cancellationToken.IsCancellationRequested)
                    throw;
            }
        }
        holder.Start();
        return holder;
    }

    public abstract Task<ClusterLockInfo?> TryQuery(Symbol key, CancellationToken cancellationToken);
    public abstract Task<Moment?> TryAcquire(Symbol key, string value, TimeSpan expiresIn, CancellationToken cancellationToken);
    public abstract Task<bool> TryRenew(Symbol key, string value, TimeSpan expiresIn, CancellationToken cancellationToken);
    public abstract Task<bool> TryRelease(Symbol key, string value, CancellationToken cancellationToken);
    public abstract Task WhenChanged(Symbol key, CancellationToken cancellationToken);

    // Protected methods

    protected virtual ClusterLockHolder CreateHolder(Symbol key, string value, ClusterLockOptions options)
        => new(this, key, value, NextHolderId(), options);

    protected virtual string NextHolderId()
        => ZString.Concat(HolderKeyPrefix, Interlocked.Increment(ref LastHolderId));

}
