using ActualChat.Mesh;

namespace ActualChat.Redis;

public static class MeshLocksExt
{
    public static async Task<T> Run<T>(
        this IMeshLocks meshLocks,
        Func<CancellationToken, Task<T>> taskFactory,
        string key,
        MeshLockOptions options,
        CancellationToken cancellationToken)
    {
        var (_, result) = await meshLocks.RunInternal(taskFactory,
                key,
                options,
                true,
                cancellationToken)
            .ConfigureAwait(false);
        return result;
    }

    public static Task<T> Run<T>(
        this IMeshLocks meshLocks,
        Func<CancellationToken, Task<T>> taskFactory,
        string key,
        CancellationToken cancellationToken)
        => meshLocks.Run(taskFactory, key, GetDefaultOptions(), cancellationToken);

    public static Task Run(
        this IMeshLocks meshLocks,
        Func<CancellationToken, Task> taskFactory,
        string key,
        MeshLockOptions options,
        CancellationToken cancellationToken)
        => meshLocks.Run(taskFactory.ToUnitTaskFactory(),
            key,
            options,
            cancellationToken);

    public static Task Run(
        this IMeshLocks meshLocks,
        Func<CancellationToken, Task> taskFactory,
        string key,
        CancellationToken cancellationToken)
        => meshLocks.Run(taskFactory.ToUnitTaskFactory(),
            key,
            GetDefaultOptions(),
            cancellationToken);

    public static Task<(bool WasRun, T Result)> TryRun<T>(
        this IMeshLocks meshLocks,
        Func<CancellationToken, Task<T>> taskFactory,
        string key,
        MeshLockOptions options,
        CancellationToken cancellationToken)
        => meshLocks.RunInternal(taskFactory,
            key,
            options,
            false,
            cancellationToken);

    public static async Task<bool> TryRun(
        this IMeshLocks meshLocks,
        Func<CancellationToken, Task> taskFactory,
        string key,
        MeshLockOptions options,
        CancellationToken cancellationToken)
    {
        var (isStarted, _) = await meshLocks.TryRun(taskFactory.ToUnitTaskFactory(), key, options, cancellationToken)
            .ConfigureAwait(false);
        return isStarted;
    }

    public static Task<bool> TryRun(
        this IMeshLocks meshLocks,
        Func<CancellationToken, Task> taskFactory,
        string key,
        CancellationToken cancellationToken)
        => meshLocks.TryRun(taskFactory, key, GetDefaultOptions(), cancellationToken);

    private static async Task<(bool WasRun, T Result)> RunInternal<T>(
        this IMeshLocks meshLocks,
        Func<CancellationToken, Task<T>> taskFactory,
        string key,
        MeshLockOptions options,
        bool waitLock,
        CancellationToken cancellationToken)
    {
        var meshLockHolder = await Lock()
            .ConfigureAwait(false);
        if (meshLockHolder is null)
            return (false, default!);

        await using var _2 = meshLockHolder.ConfigureAwait(false);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, meshLockHolder.StopToken);
        var result = await taskFactory(cts.Token).ConfigureAwait(false);
        return (true, result);

        async Task<MeshLockHolder?> Lock()
            => !waitLock
                ? await meshLocks
                    .TryLock(key, "", options, cancellationToken)
                    .ConfigureAwait(false)
                : await meshLocks
                    .Lock(key, "", options, cancellationToken)
                    .ConfigureAwait(false);
    }

    private static MeshLockOptions GetDefaultOptions() => new (TimeSpan.FromSeconds(15));
}
