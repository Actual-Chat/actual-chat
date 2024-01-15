namespace ActualChat.Mesh;

public interface IMeshLocks<TContext> : IMeshLocks;

public interface IMeshLocks
{
    MeshLockOptions LockOptions { get; }
    IMomentClock Clock { get; }
    IMeshLocksBackend Backend { get; }

    Task<MeshLockInfo?> TryQuery(Symbol key, CancellationToken cancellationToken = default);
    Task<MeshLockHolder> Acquire(Symbol key, string value, MeshLockOptions lockOptions, CancellationToken cancellationToken = default);
    Task<Task> WhenChanged(Symbol key, CancellationToken cancellationToken = default);
}

public interface IMeshLocksBackend : IMeshLocks
{
    ILogger? Log { get; }

    Task<bool> TryRenew(Symbol key, string value, TimeSpan expiresIn, CancellationToken cancellationToken);
    Task<MeshLockReleaseResult> TryRelease(Symbol key, string value, CancellationToken cancellationToken);
    Task<bool> ForceRelease(Symbol key, bool mustNotify, CancellationToken cancellationToken);
}
