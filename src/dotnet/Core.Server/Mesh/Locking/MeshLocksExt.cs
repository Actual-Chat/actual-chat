using ActualLab.Generators;

namespace ActualChat.Mesh;

public static partial class MeshLocksExt
{
    public static readonly Generator<string> DefaultValueGenerator = Alphabet.AlphaNumeric.Generator8;

    // WithXxx

    public static IMeshLocks WithKeyPrefix(this IMeshLocks meshLocks, string keyPrefix)
        => meshLocks.With(keyPrefix, null);

    public static IMeshLocks WithLockOptions(this IMeshLocks meshLocks, MeshLockOptions lockOptions)
        => meshLocks.With("", lockOptions);

    // TryLock - w/o value

    public static Task<MeshLockHolder?> TryLock(this IMeshLocks meshLocks,
        string key,
        CancellationToken cancellationToken = default)
        => meshLocks.TryLock(key, DefaultValueGenerator.Next(), meshLocks.LockOptions, cancellationToken);

    public static Task<MeshLockHolder?> TryLock(this IMeshLocks meshLocks,
        string key, MeshLockOptions lockOptions,
        CancellationToken cancellationToken = default)
        => meshLocks.TryLock(key, DefaultValueGenerator.Next(), lockOptions, cancellationToken);

    // TryLock - with value

    public static Task<MeshLockHolder?> TryLock(this IMeshLocks meshLocks,
        string key, string value,
        CancellationToken cancellationToken = default)
        => meshLocks.TryLock(key, value, meshLocks.LockOptions, cancellationToken);

    // Lock - w/o value

    public static Task<MeshLockHolder> Lock(this IMeshLocks meshLocks,
        string key,
        CancellationToken cancellationToken = default)
        => meshLocks.Lock(key, DefaultValueGenerator.Next(), meshLocks.LockOptions, cancellationToken);

    public static Task<MeshLockHolder> Lock(this IMeshLocks meshLocks,
        string key, MeshLockOptions lockOptions,
        CancellationToken cancellationToken = default)
        => meshLocks.Lock(key, DefaultValueGenerator.Next(), lockOptions, cancellationToken);

    public static Task<MeshLockHolder> Lock(this IMeshLocks meshLocks,
        string key, TimeSpan lockTimeout,
        CancellationToken cancellationToken = default)
        => meshLocks.Lock(key, DefaultValueGenerator.Next(), meshLocks.LockOptions, lockTimeout, cancellationToken);

    public static Task<MeshLockHolder> Lock(this IMeshLocks meshLocks,
        string key, MeshLockOptions lockOptions, TimeSpan lockTimeout,
        CancellationToken cancellationToken = default)
        => meshLocks.Lock(key, DefaultValueGenerator.Next(), lockOptions, lockTimeout, cancellationToken);

    // Lock - with value

    public static Task<MeshLockHolder> Lock(this IMeshLocks meshLocks,
        string key, string value,
        CancellationToken cancellationToken = default)
        => meshLocks.Lock(key, value, meshLocks.LockOptions, cancellationToken);

    public static Task<MeshLockHolder> Lock(this IMeshLocks meshLocks,
        string key, string value, TimeSpan lockTimeout,
        CancellationToken cancellationToken = default)
        => meshLocks.Lock(key, value, meshLocks.LockOptions, lockTimeout, cancellationToken);

    public static async Task<MeshLockHolder> Lock(this IMeshLocks meshLocks,
        string key, string value, MeshLockOptions lockOptions, TimeSpan lockTimeout,
        CancellationToken cancellationToken = default)
    {
        if (lockTimeout == TimeSpan.MaxValue)
            return await meshLocks.Lock(key, value, lockOptions, cancellationToken).ConfigureAwait(false);

        var timeoutCts = new CancellationTokenSource(lockTimeout);
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
