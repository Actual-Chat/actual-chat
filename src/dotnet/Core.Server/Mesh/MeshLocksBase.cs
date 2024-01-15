using Cysharp.Text;

namespace ActualChat.Mesh;

public abstract class MeshLocksBase(IMomentClock? clock = null, ILogger? log = null) : IMeshLocksBackend
{
    protected readonly string HolderKeyPrefix = Alphabet.AlphaNumeric.Generator8.Next() + "-";
    protected long LastHolderId;
    protected ILogger? Log { get; init; } = log;

    public MeshLockOptions LockOptions { get; init; } = new(TimeSpan.FromSeconds(10));
    public IMomentClock Clock { get; init; } = clock ?? MomentClockSet.Default.SystemClock;
    public IMeshLocksBackend Backend => this;
    ILogger? IMeshLocksBackend.Log => Log;

    public virtual async Task<MeshLockHolder> Acquire(
        Symbol key, string value,
        MeshLockOptions lockOptions,
        CancellationToken cancellationToken = default)
    {
        var holder = CreateHolder(key, value, lockOptions);
        Log?.LogInformation("Acquire: {Key} = {StoredValue}", key, holder.StoredValue);
        Task? whenChanged = null;
        var whenChangedCts = cancellationToken.CreateLinkedTokenSource();
        try {
            while (true) {
                cancellationToken.ThrowIfCancellationRequested();
                whenChanged ??= await WhenChanged(key, whenChangedCts.Token).ConfigureAwait(false);
                var isAcquired = await TryAcquire(key, holder.StoredValue, lockOptions.ExpirationPeriod, cancellationToken).ConfigureAwait(false);
                if (isAcquired)
                    break;

                try {
                    // It's fine to use CancellationToken.None here:
                    // whenChanged already depends on cancellationToken via whenChangedCts.
                    await whenChanged.WaitAsync(lockOptions.CheckPeriod, CancellationToken.None).ConfigureAwait(false);
                    whenChanged = null;
                }
                catch (TimeoutException) { }
            }
        }
        catch (Exception e) {
            Log?.LogError(e, "Acquire: {Key} = {StoredValue}", key, holder.StoredValue);
            throw;
        }
        finally {
            whenChangedCts.CancelAndDisposeSilently();
        }
        holder.Start();
        return holder;
    }

    public abstract Task<MeshLockInfo?> TryQuery(Symbol key, CancellationToken cancellationToken = default);
    public abstract Task<Task> WhenChanged(Symbol key, CancellationToken cancellationToken = default);

    Task<bool> IMeshLocksBackend.TryRenew(Symbol key, string value, TimeSpan expiresIn, CancellationToken cancellationToken)
        => TryRenew(key, value, expiresIn, cancellationToken);
    Task<MeshLockReleaseResult> IMeshLocksBackend.TryRelease(Symbol key, string value, CancellationToken cancellationToken)
        => TryRelease(key, value, cancellationToken);
    Task<bool> IMeshLocksBackend.ForceRelease(Symbol key, bool mustNotify, CancellationToken cancellationToken)
        => ForceRelease(key, mustNotify, cancellationToken);

    // Protected methods

    protected virtual MeshLockHolder CreateHolder(Symbol key, string value, MeshLockOptions options)
        => new(this, key, value, NextHolderId(), options);

    protected virtual string NextHolderId()
        => ZString.Concat(HolderKeyPrefix, Interlocked.Increment(ref LastHolderId));

    protected abstract Task<bool> TryAcquire(Symbol key, string value, TimeSpan expiresIn, CancellationToken cancellationToken);
    protected abstract Task<bool> TryRenew(Symbol key, string value, TimeSpan expiresIn, CancellationToken cancellationToken);
    protected abstract Task<MeshLockReleaseResult> TryRelease(Symbol key, string value, CancellationToken cancellationToken);
    protected abstract Task<bool> ForceRelease(Symbol key, bool mustNotify, CancellationToken cancellationToken);
}
