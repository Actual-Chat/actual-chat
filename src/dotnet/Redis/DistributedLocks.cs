namespace ActualChat.Redis;

public class DistributedLocks<TContext>(IServiceProvider services)
{
    public record Options(TimeSpan Ttl, TimeSpan RefreshInterval)
    {
        public static readonly Options Default = new (TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(15));
    }

    public async Task<T> Run<T>(
        Func<CancellationToken, Task<T>> taskFactory,
        string key,
        Options options,
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
        => Run(taskFactory, key, Options.Default, cancellationToken);

    public Task Run(
        Func<CancellationToken, Task> taskFactory,
        string key,
        Options options,
        CancellationToken cancellationToken)
        => Run(ct => taskFactory(ct).ContinueWith(_ => Unit.Default, TaskScheduler.Current),
            key,
            options,
            cancellationToken);

    public Task Run(
        Func<CancellationToken, Task> taskFactory,
        string key,
        CancellationToken cancellationToken)
        => Run(ct => ToTaskWithResult(taskFactory, ct),
            key,
            Options.Default,
            cancellationToken);

    public Task<(bool WasRun, T Result)> TryRun<T>(Func<CancellationToken, Task<T>> taskFactory, string key, Options options, CancellationToken cancellationToken)
        => RunInternal(taskFactory, key, options, false, cancellationToken);

    public async Task<bool> TryRun(Func<CancellationToken, Task> taskFactory, string key, Options options, CancellationToken cancellationToken)
    {
        var (isStarted, _) = await TryRun(ct => ToTaskWithResult(taskFactory, ct), key, options, cancellationToken)
            .ConfigureAwait(false);
        return isStarted;
    }

    public Task<bool> TryRun(
        Func<CancellationToken, Task> taskFactory,
        string key,
        CancellationToken cancellationToken)
        => TryRun(taskFactory, key, Options.Default, cancellationToken);

    private static Task<Unit> ToTaskWithResult(Func<CancellationToken, Task> taskFactory, CancellationToken ct)
        => taskFactory(ct).ContinueWith(_ => Unit.Default, TaskScheduler.Current);

    private async Task<(bool WasRun, T Result)> RunInternal<T>(
        Func<CancellationToken, Task<T>> taskFactory,
        string key,
        Options options,
        bool waitLock,
        CancellationToken cancellationToken)
    {
        using var _1 = Tracer.Default.Region($"{nameof(DistributedLocks<TContext>)}.{nameof(RunInternal)}(): {typeof(TContext).Name}.{key}");
        var redisLock = new RedisLock<TContext>(services, key);
        if (waitLock)
            await redisLock.Wait(options.Ttl, cancellationToken).ConfigureAwait(false);
        else if (!await redisLock.TryLock(options.Ttl).ConfigureAwait(false))
            return (false, default!);
        await using var _2 = redisLock.ConfigureAwait(false);

        var cts = cancellationToken.CreateLinkedTokenSource();
        var keepLockTask = redisLock.Keep(options.Ttl, options.RefreshInterval, cancellationToken);
        var jobTask = taskFactory(cts.Token);
        try {
            await Task.WhenAny(keepLockTask, jobTask).ConfigureAwait(false);
        }
        finally {
            await cts.CancelAsync().ConfigureAwait(false);
        }
        return (true, await jobTask.ConfigureAwait(false));
    }
}
