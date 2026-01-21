namespace ActualChat.Mesh;

public static partial class MeshLocksExt
{
    // LockAndRun - with Task

    public static Task LockAndRun(
        this IMeshLocks meshLocks,
        string key,
        Func<CancellationToken, Task> taskFactory,
        CancellationToken cancellationToken = default)
        => meshLocks.LockAndRun(key, taskFactory, null, TimeSpan.MaxValue, cancellationToken);

    public static Task LockAndRun(
        this IMeshLocks meshLocks,
        string key,
        Func<CancellationToken, Task> taskFactory,
        MeshLockOptions? lockOptions,
        CancellationToken cancellationToken = default)
        => meshLocks.LockAndRun(key, taskFactory, lockOptions, TimeSpan.MaxValue, cancellationToken);

    public static async Task LockAndRun(this IMeshLocks meshLocks,
        string key,
        Func<CancellationToken, Task> taskFactory,
        MeshLockOptions? lockOptions,
        TimeSpan lockTimeout,
        CancellationToken cancellationToken = default)
    {
        var holder = await meshLocks.Lock(key, lockOptions, lockTimeout, cancellationToken).ConfigureAwait(false);
        var linkedCts = cancellationToken.LinkWith(holder.StopToken);
        try {
            await taskFactory.Invoke(linkedCts.Token).ConfigureAwait(false);
        }
        finally {
            linkedCts.CancelAndDisposeSilently();
            await holder.DisposeAsync().ConfigureAwait(false);
        }
    }

    // LockAndRun - with Task<T>

    public static Task<T> LockAndRun<T>(this IMeshLocks meshLocks,
        string key,
        Func<CancellationToken, Task<T>> taskFactory,
        CancellationToken cancellationToken = default)
        => meshLocks.LockAndRun(key, taskFactory, null, TimeSpan.MaxValue, cancellationToken);

    public static Task<T> LockAndRun<T>(this IMeshLocks meshLocks,
        string key,
        Func<CancellationToken, Task<T>> taskFactory,
        MeshLockOptions? lockOptions,
        CancellationToken cancellationToken = default)
        => meshLocks.LockAndRun(key, taskFactory, lockOptions, TimeSpan.MaxValue, cancellationToken);

    public static async Task<T> LockAndRun<T>(this IMeshLocks meshLocks,
        string key,
        Func<CancellationToken, Task<T>> taskFactory,
        MeshLockOptions? lockOptions,
        TimeSpan lockTimeout,
        CancellationToken cancellationToken = default)
    {
        var lockHolder = await meshLocks.Lock(key, lockOptions, lockTimeout, cancellationToken).ConfigureAwait(false);
        var linkedCts = cancellationToken.LinkWith(lockHolder.StopToken);
        try {
            return await taskFactory.Invoke(linkedCts.Token).ConfigureAwait(false);
        }
        finally {
            linkedCts.CancelAndDisposeSilently();
            await lockHolder.DisposeAsync().ConfigureAwait(false);
        }
    }
}
