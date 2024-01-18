namespace ActualChat.Mesh;

public interface IMeshLocks<TContext> : IMeshLocks;

public interface IMeshLocks
{
    MeshLockOptions LockOptions { get; }
    IMomentClock Clock { get; }
    IMeshLocksBackend Backend { get; }

    // Methods MUST auto-retry in case they can't reach the lock service
    Task<MeshLockInfo?> GetInfo(string key, CancellationToken cancellationToken = default);
    Task<MeshLockHolder?> TryLock(string key, string value, MeshLockOptions lockOptions, CancellationToken cancellationToken = default);
    Task<MeshLockHolder> Lock(string key, string value, MeshLockOptions lockOptions, CancellationToken cancellationToken = default);
    Task<IAsyncSubscription<string>> Changes(string key, CancellationToken cancellationToken = default);
}

public interface IMeshLocksBackend : IMeshLocks
{
    ILogger? Log { get; }

    // Methods MUST NOT auto-retry in case they can't reach the lock service
    Task<bool> TryRenew(string key, string value, TimeSpan expiresIn, CancellationToken cancellationToken = default);
    Task<MeshLockReleaseResult> TryRelease(string key, string value, CancellationToken cancellationToken = default);

    // Methods below must be used only in tests
    Task<bool> ForceRelease(string key, bool mustNotify, CancellationToken cancellationToken = default);
}
