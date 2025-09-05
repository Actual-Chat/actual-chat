using ActualLab.Diagnostics;

namespace ActualChat;

public abstract class OldShardWorker(IServiceProvider services, ShardScheme shardScheme, string? keyPrefix = null)
    : WorkerBase, IHasServices
{
    private static bool DebugMode => Constants.DebugMode.OldShardWorker;

    [field: AllowNull, MaybeNull]
    protected ILogger Log => field ??= Services.LoggerFactory().CreateLogger(GetType(), $"({KeyPrefix}.{ShardScheme.Id.Value})");
    protected ILogger? DebugLog => DebugMode ? Log.IfEnabled(LogLevel.Debug) : null;

    public IServiceProvider Services { get; } = services;
    [field: AllowNull, MaybeNull]
    public ShardLocker ShardLocker => field ??= Services.ShardLockers()[shardScheme, keyPrefix];
    public ShardScheme ShardScheme => ShardLocker.ShardScheme;
    public string KeyPrefix => ShardLocker.KeyPrefix;

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        await using var _ = ShardLocker.Schedule(OnRun).ConfigureAwait(false);
        await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, cancellationToken).SilentAwait(false);
    }

    protected abstract Task OnRun(int shardIndex, CancellationToken cancellationToken);
}
