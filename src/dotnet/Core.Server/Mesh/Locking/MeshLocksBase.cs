using Cysharp.Text;

namespace ActualChat.Mesh;

public abstract class MeshLocksBase(IMomentClock? clock = null, ILogger? log = null) : IMeshLocksBackend
{
    public static readonly MeshLockOptions DefaultLockOptions = new(TimeSpan.FromSeconds(15));
    public static readonly TimeSpan DefaultUnconditionalCheckPeriod = TimeSpan.FromSeconds(10);

    protected readonly string HolderKeyPrefix = Alphabet.AlphaNumeric.Generator8.Next() + "-";
    protected long LastHolderId;
    ILogger? IMeshLocksBackend.Log => Log;
    protected ILogger? Log { get; init; } = log;

    public MeshLockOptions LockOptions { get; init; } = DefaultLockOptions;
    public TimeSpan UnconditionalCheckPeriod { get; init; } = DefaultUnconditionalCheckPeriod;
    public RetryDelaySeq RetryDelays { get; init; } = RetryDelaySeq.Exp(0.5, 10);

    public IMomentClock Clock { get; init; } = clock ?? MomentClockSet.Default.SystemClock;
    public IMeshLocksBackend Backend => this;

    public virtual async Task<MeshLockHolder?> TryLock(
        string key, string value,
        MeshLockOptions lockOptions,
        CancellationToken cancellationToken = default)
    {
        var holder = CreateHolder(key, value, lockOptions);
        Log?.LogInformation("TryLock: {Key} = {StoredValue}", key, holder.StoredValue);
        try {
            cancellationToken.ThrowIfCancellationRequested();
            var isAcquired = await TryLock(key, holder.StoredValue, lockOptions.ExpirationPeriod, cancellationToken).ConfigureAwait(false);
            if (!isAcquired)
                return null;
        }
        catch (Exception e) {
            if (e is OperationCanceledException)
                Log?.LogInformation("TryLock cancelled: {Key} = {StoredValue}", key, holder.StoredValue);
            else
                Log?.LogError(e, "TryLock failed: {Key} = {StoredValue}", key, holder.StoredValue);
            throw;
        }
        holder.Start();
        return holder;
    }

    public virtual async Task<MeshLockHolder> Lock(
        string key, string value,
        MeshLockOptions lockOptions,
        CancellationToken cancellationToken = default)
    {
        var holder = CreateHolder(key, value, lockOptions);
        Log?.LogInformation("Lock: {Key} = {StoredValue}", key, holder.StoredValue);
        IAsyncSubscription<string>? changes = null;
        try {
            var consumeTask = (Task<bool>?)null;
            while (true) {
                changes ??= await Changes(key, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                var isAcquired = await TryLock(key, holder.StoredValue, lockOptions.ExpirationPeriod, cancellationToken).ConfigureAwait(false);
                if (isAcquired)
                    break;

                try {
                    consumeTask ??= changes.Reader.WaitToReadAndConsumeAsync(cancellationToken);
                    // It's fine to use CancellationToken.None here:
                    // consumeTask already depends on cancellationToken.
                    var canRead = await consumeTask
                        .WaitAsync(UnconditionalCheckPeriod, CancellationToken.None)
                        .ConfigureAwait(false);
                    // It's important to throw on cancellation here: canRead may return false exactly due to this
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!canRead) {
                        // Something is off, prob. Redis disconnect - we need to restart
                        await changes.DisposeSilentlyAsync().ConfigureAwait(false);
                        changes = null;
                        consumeTask = null;
                        continue;
                    }
                    consumeTask = null;
                }
                catch (TimeoutException) { }
            }
        }
        catch (Exception e) {
            if (e.IsCancellationOf(cancellationToken))
                Log?.LogInformation("Lock cancelled: {Key} = {StoredValue}", key, holder.StoredValue);
            else
                Log?.LogError(e, "Lock failed: {Key} = {StoredValue}", key, holder.StoredValue);
            throw;
        }
        finally {
            await changes.DisposeSilentlyAsync().ConfigureAwait(false);
        }
        holder.Start();
        return holder;
    }

    public abstract Task<MeshLockInfo?> GetInfo(string key, CancellationToken cancellationToken = default);
    public abstract Task<IAsyncSubscription<string>> Changes(string key, CancellationToken cancellationToken = default);
    public abstract Task<string[]> ListKeys(string prefix, CancellationToken cancellationToken = default);
    public abstract IMeshLocks WithKeyPrefix(string keyPrefix);

    Task<bool> IMeshLocksBackend.TryRenew(string key, string value, TimeSpan expiresIn, CancellationToken cancellationToken)
        => TryRenew(key, value, expiresIn, cancellationToken);
    Task<MeshLockReleaseResult> IMeshLocksBackend.TryRelease(string key, string value, CancellationToken cancellationToken)
        => TryRelease(key, value, cancellationToken);
    Task<bool> IMeshLocksBackend.ForceRelease(string key, bool mustNotify, CancellationToken cancellationToken)
        => ForceRelease(key, mustNotify, cancellationToken);

    // Protected methods

    protected virtual MeshLockHolder CreateHolder(string key, string value, MeshLockOptions options)
        => new(this, NextHolderId(), key, value, options);

    protected virtual string NextHolderId()
        => ZString.Concat(HolderKeyPrefix, Interlocked.Increment(ref LastHolderId));

    protected abstract Task<bool> TryLock(string key, string value, TimeSpan expiresIn, CancellationToken cancellationToken);
    protected abstract Task<bool> TryRenew(string key, string value, TimeSpan expiresIn, CancellationToken cancellationToken);
    protected abstract Task<MeshLockReleaseResult> TryRelease(string key, string value, CancellationToken cancellationToken);
    protected abstract Task<bool> ForceRelease(string key, bool mustNotify, CancellationToken cancellationToken);
}
