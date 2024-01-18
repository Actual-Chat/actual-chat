namespace ActualChat.Mesh;

public static class MeshLocksExt
{
    // TryLock

    public static Task<MeshLockHolder?> TryLock(this IMeshLocks meshLocks,
        string key, string value,
        CancellationToken cancellationToken = default)
        => meshLocks.TryLock(key, value, meshLocks.LockOptions, cancellationToken);

    // Lock

    public static Task<MeshLockHolder> Lock(this IMeshLocks meshLocks,
        string key, string value,
        CancellationToken cancellationToken = default)
        => meshLocks.Lock(key, value, meshLocks.LockOptions, cancellationToken);

    public static Task<MeshLockHolder> Lock(this IMeshLocks meshLocks,
        string key, string value, TimeSpan timeout,
        CancellationToken cancellationToken = default)
        => meshLocks.Lock(key, value, meshLocks.LockOptions, timeout, cancellationToken);

    public static async Task<MeshLockHolder> Lock(this IMeshLocks meshLocks,
        string key, string value, MeshLockOptions lockOptions, TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (timeout == TimeSpan.MaxValue)
            return await meshLocks.Lock(key, value, lockOptions, cancellationToken).ConfigureAwait(false);

        var timeoutCts = new CancellationTokenSource(timeout);
        var cts = timeoutCts.Token.LinkWith(cancellationToken);
        try {
            return await meshLocks.Lock(key, value, lockOptions, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested) {
            if (!cancellationToken.IsCancellationRequested)
                throw new TimeoutException("Timed out while acquiring a cluster lock.");
            throw;
        }
        finally {
            cts.Dispose();
            timeoutCts.Dispose();
        }
    }
}
