using ActualLab.Generators;

namespace ActualChat.Mesh;

public static partial class MeshLocksExt
{
    public static readonly Generator<string> DefaultValueGenerator = Alphabet.AlphaNumeric.Generator8;

    // WithXxx

    public static IMeshLocks With(this IMeshLocks meshLocks, string keyPrefix, Func<MeshLockOptions, MeshLockOptions> lockOptionsBuilder)
        => meshLocks.With(keyPrefix, lockOptionsBuilder.Invoke(meshLocks.LockOptions));

    public static IMeshLocks WithKeyPrefix(this IMeshLocks meshLocks, string keyPrefix)
        => meshLocks.With(keyPrefix, null);

    public static IMeshLocks WithLockOptions(this IMeshLocks meshLocks, MeshLockOptions lockOptions)
        => meshLocks.With("", lockOptions);

    public static IMeshLocks WithLockOptions(this IMeshLocks meshLocks, Func<MeshLockOptions, MeshLockOptions> lockOptionsBuilder)
        => meshLocks.With("", lockOptionsBuilder.Invoke(meshLocks.LockOptions));

    // TryLock

    public static Task<MeshLockHolder?> TryLock(this IMeshLocks meshLocks,
        string key,
        CancellationToken cancellationToken = default)
        => meshLocks.TryLock(key, null, cancellationToken);

    public static async Task<MeshLockHolder?> TryLock(this IMeshLocks meshLocks,
        string key, MeshLockOptions? lockOptions, TimeSpan lockTimeout,
        CancellationToken cancellationToken = default)
    {
        if (lockTimeout == TimeSpan.MaxValue)
            return await meshLocks.Lock(key, lockOptions, cancellationToken).ConfigureAwait(false);

        var timeoutCts = cancellationToken.CreateLinkedTokenSource();
        var timeoutToken = timeoutCts.Token;
        timeoutCts.CancelAfter(lockTimeout);
        try {
            return await meshLocks.TryLock(key, lockOptions, timeoutToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException e) when (e.IsCancellationOfTimeoutToken(timeoutToken, cancellationToken)) {
            throw new TimeoutException("Couldn't acquire the mesh lock within the specified timeout.");
        }
        finally {
            timeoutCts.CancelAndDisposeSilently();
        }
    }

    // Lock

    public static Task<MeshLockHolder> Lock(this IMeshLocks meshLocks,
        string key,
        CancellationToken cancellationToken = default)
        => meshLocks.Lock(key, null, cancellationToken);

    public static async Task<MeshLockHolder> Lock(this IMeshLocks meshLocks,
        string key, MeshLockOptions? lockOptions, TimeSpan lockTimeout,
        CancellationToken cancellationToken = default)
    {
        if (lockTimeout == TimeSpan.MaxValue)
            return await meshLocks.Lock(key, lockOptions, cancellationToken).ConfigureAwait(false);

        var timeoutCts = cancellationToken.CreateLinkedTokenSource();
        var timeoutToken = timeoutCts.Token;
        timeoutCts.CancelAfter(lockTimeout);
        try {
            return await meshLocks.Lock(key, lockOptions, timeoutToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException e) when (e.IsCancellationOfTimeoutToken(timeoutToken, cancellationToken)) {
            throw new TimeoutException("Couldn't acquire the mesh lock within the specified timeout.");
        }
        finally {
            timeoutCts.CancelAndDisposeSilently();
        }
    }
}
