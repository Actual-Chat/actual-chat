using ActualLab.Resilience;

namespace ActualChat.Mesh;

public interface IMeshLocks : IHasServices
{
    MeshLockOptions LockOptions { get; }
    RetryDelaySeq RetryDelays { get; }

    MomentClock Clock { get; }
    IMeshLocksBackend Backend { get; }

    // Methods MUST auto-retry in case they can't reach the lock service
    string GetFullKey(string key);
    Task<MeshLockInfo?> GetInfo(string key, CancellationToken cancellationToken = default);
    Task<MeshLockHolder?> TryLock(string key, MeshLockOptions? lockOptions, CancellationToken cancellationToken = default);
    Task<MeshLockHolder?> TryForceReacquire(string key, string expectedHolderId, MeshLockOptions? lockOptions, CancellationToken cancellationToken = default);
    Task<MeshLockHolder> Lock(string key, MeshLockOptions? lockOptions, CancellationToken cancellationToken = default);
    Task<IAsyncSubscription<string>> Changes(string key, CancellationToken cancellationToken = default);
    Task<List<string>> ListKeys(string prefix, CancellationToken cancellationToken = default);
    IMeshLocks With(string keyPrefix, MeshLockOptions? lockOptions);
}

public interface IMeshLocksBackend : IMeshLocks
{
    ILogger Log { get; }
    ILogger? DebugLog { get; }
    ChaosMaker ChaosMaker { get; }

    // Methods MUST NOT auto-retry in case they can't reach the lock service
    Task<bool> TryRenew(string key, string value, TimeSpan expiresIn, CancellationToken cancellationToken = default);
    Task<MeshLockReleaseResult> TryRelease(string key, string value, CancellationToken cancellationToken = default);

    // Used in tests and for fast shard takeover when a node is confirmed dead (e.g. by K8s EndpointSlice watch)
    Task<bool> ForceRelease(string key, bool mustNotify, CancellationToken cancellationToken = default);

    // Atomically transfers lock ownership using CAS: only succeeds if current holder matches expectedHolderId.
    // Used for race-safe shard takeover when a node is confirmed dead.
    Task<bool> ForceReacquire(string key, string expectedHolderId, string newHolderId, TimeSpan expiresIn, CancellationToken cancellationToken = default);
}
