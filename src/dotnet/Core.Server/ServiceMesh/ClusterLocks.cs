namespace ActualChat.ServiceMesh;

public sealed class ClusterLocks
{
    public ClusterLocksBackend Backend { get; }
    public ClusterLockOptions DefaultOptions => Backend.DefaultOptions;

    public ClusterLocks(ClusterLocksBackend backend)
    {
        Backend = backend;
        DefaultOptions.RequireValid();
    }

    public Task<ClusterLockInfo?> TryQuery(Symbol key, CancellationToken cancellationToken = default)
        => Backend.TryQuery(key, cancellationToken);

    public Task<ClusterLockHolder> Acquire(
        Symbol key, string value,
        ClusterLockOptions options,
        CancellationToken cancellationToken = default)
        => Backend.Acquire(key, value, options, cancellationToken);
}
