using ActualLab.Diagnostics;

namespace ActualChat;

public abstract class LegacyShardWorker(IServiceProvider services, ShardScheme shardScheme, string? keyPrefix = null)
    : WorkerBase, IHasServices
{
    private static bool DebugMode => Constants.DebugMode.OldShardWorker;

    [field: AllowNull, MaybeNull]
    protected ILogger Log => field ??= Services.LoggerFactory().CreateLogger(GetType(), $"({KeyPrefix}.{ShardScheme.Id.Value})");
    protected ILogger? DebugLog => DebugMode ? Log.IfEnabled(LogLevel.Debug) : null;

    public IServiceProvider Services { get; } = services;
    public ShardScheduler ShardScheduler { get; } = services.ShardSchedulers()[shardScheme, keyPrefix];
    public ShardScheme ShardScheme => ShardScheduler.ShardScheme;
    public string KeyPrefix => ShardScheduler.KeyPrefix;

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        await using var _ = ShardScheduler.Schedule(OnRun).ConfigureAwait(false);
        await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, cancellationToken).SilentAwait(false);
    }

    protected abstract Task OnRun(int shardIndex, CancellationToken cancellationToken);
}
