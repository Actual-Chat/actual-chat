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
        options = options.WithDefaults(DefaultOptions).RequireValid();
        var holder = CreateHolder(key, value, options);
        Task? whenChanged = null;
        var whenChangedCts = cancellationToken.CreateLinkedTokenSource();
        try {
            while (true) {
                cancellationToken.ThrowIfCancellationRequested();
                whenChanged ??= await WhenChanged(key, whenChangedCts.Token).ConfigureAwait(false);
                var isAcquired = await TryAcquire(key, holder.StoredValue, options.ExpirationPeriod, cancellationToken).ConfigureAwait(false);
                if (isAcquired)
                    break;

                try {
                    // It's fine to use CancellationToken.None here:
                    // whenChanged already depends on cancellationToken via whenChangedCts.
                    await whenChanged.WaitAsync(options.CheckPeriod, CancellationToken.None).ConfigureAwait(false);
                    whenChanged = null;
                }
                catch (TimeoutException) { }
            }
        }
        finally {
            whenChangedCts.CancelAndDisposeSilently();
        }
        holder.Start();
        return holder;
    }

    public abstract Task<ClusterLockInfo?> TryQuery(Symbol key, CancellationToken cancellationToken);
    public abstract Task<bool> TryAcquire(Symbol key, string value, TimeSpan expiresIn, CancellationToken cancellationToken);
    public abstract Task<bool> TryRenew(Symbol key, string value, TimeSpan expiresIn, CancellationToken cancellationToken);
    public abstract Task<bool> TryRelease(Symbol key, string value, CancellationToken cancellationToken);
    public abstract Task<Task> WhenChanged(Symbol key, CancellationToken cancellationToken);

    // Protected methods

    protected virtual ClusterLockHolder CreateHolder(Symbol key, string value, ClusterLockOptions options)
        => new(this, key, value, NextHolderId(), options);

    protected virtual string NextHolderId()
        => ZString.Concat(HolderKeyPrefix, Interlocked.Increment(ref LastHolderId));

}
