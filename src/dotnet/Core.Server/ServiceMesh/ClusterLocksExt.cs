namespace ActualChat.ServiceMesh;

public static class ClusterLocksExt
{
    public static Task<ClusterLockHolder> Acquire(this ClusterLocks clusterLocks,
        Symbol key, string value,
        CancellationToken cancellationToken = default)
        => clusterLocks.Acquire(key, value, clusterLocks.DefaultOptions, cancellationToken);

    public static Task<ClusterLockHolder> Acquire(this ClusterLocks clusterLocks,
        Symbol key, string value, TimeSpan timeout,
        CancellationToken cancellationToken = default)
        => clusterLocks.Acquire(key, value, clusterLocks.DefaultOptions, timeout, cancellationToken);

    public static async Task<ClusterLockHolder> Acquire(this ClusterLocks clusterLocks,
        Symbol key, string value, ClusterLockOptions options, TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (timeout == TimeSpan.MaxValue)
            return await clusterLocks.Acquire(key, value, options, cancellationToken).ConfigureAwait(false);

        var timeoutCts = new CancellationTokenSource(timeout);
        var cts = timeoutCts.Token.LinkWith(cancellationToken);
        try {
            return await clusterLocks.Acquire(key, value, options, cts.Token).ConfigureAwait(false);
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
