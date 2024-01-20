using ActualChat.Mesh;

namespace ActualChat.Redis;

public class DistributedLocks<TContext>(IServiceProvider services)
{
    private IMeshLocks<TContext>? _meshLocks;
    private IMeshLocks<TContext> MeshLocks => _meshLocks ??= services.MeshLocks<TContext>();

    public async Task<T> Run<T>(
        Func<CancellationToken, Task<T>> taskFactory,
        string key,
        MeshLockOptions options,
        CancellationToken cancellationToken)
    {
        var (_, result) = await RunInternal(taskFactory,
                key,
                options,
                true,
                cancellationToken)
            .ConfigureAwait(false);
        return result;
    }

    public Task<T> Run<T>(
        Func<CancellationToken, Task<T>> taskFactory,
        string key,
        CancellationToken cancellationToken)
        => Run(taskFactory, key, GetDefaultOptions(), cancellationToken);

    public Task Run(
        Func<CancellationToken, Task> taskFactory,
        string key,
        MeshLockOptions options,
        CancellationToken cancellationToken)
        => Run(taskFactory.ToUnitTaskFactory(),
            key,
            options,
            cancellationToken);

    public Task Run(
        Func<CancellationToken, Task> taskFactory,
        string key,
        CancellationToken cancellationToken)
        => Run(taskFactory.ToUnitTaskFactory(),
            key,
            GetDefaultOptions(),
            cancellationToken);

    public Task<(bool WasRun, T Result)> TryRun<T>(Func<CancellationToken, Task<T>> taskFactory, string key, MeshLockOptions options, CancellationToken cancellationToken)
        => RunInternal(taskFactory, key, options, false, cancellationToken);

    public async Task<bool> TryRun(Func<CancellationToken, Task> taskFactory, string key, MeshLockOptions options, CancellationToken cancellationToken)
    {
        var (isStarted, _) = await TryRun(taskFactory.ToUnitTaskFactory(), key, options, cancellationToken)
            .ConfigureAwait(false);
        return isStarted;
    }

    public Task<bool> TryRun(
        Func<CancellationToken, Task> taskFactory,
        string key,
        CancellationToken cancellationToken)
        => TryRun(taskFactory, key, GetDefaultOptions(), cancellationToken);

    private async Task<(bool WasRun, T Result)> RunInternal<T>(
        Func<CancellationToken, Task<T>> taskFactory,
        string key,
        MeshLockOptions options,
        bool waitLock,
        CancellationToken cancellationToken)
    {
        using var _1 = Tracer.Default.Region($"{nameof(DistributedLocks<TContext>)}.{nameof(RunInternal)}(): {typeof(TContext).Name}.{key}");
        var meshLockHolder = await Lock()
            .ConfigureAwait(false);
        if (meshLockHolder is null)
            return (false, default!);

        await using var _2 = meshLockHolder.ConfigureAwait(false);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, meshLockHolder.StopToken);
        var result = await taskFactory(cts.Token).ConfigureAwait(false);
        return (true, result);

        async Task<MeshLockHolder?> Lock()
        {
            using var _ = Tracer.Default.Region($"{nameof(DistributedLocks<TContext>)}.{nameof(RunInternal)}().Lock(): {typeof(TContext).Name}.{key}");
            return !waitLock
                ? await MeshLocks
                    .TryLock(key, Guid.NewGuid().ToString(), options, cancellationToken)
                    .ConfigureAwait(false)
                : await MeshLocks
                    .Lock(key, Guid.NewGuid().ToString(), options, cancellationToken)
                    .ConfigureAwait(false);
        }
    }


    private static MeshLockOptions GetDefaultOptions() => new (TimeSpan.FromSeconds(15));
}
