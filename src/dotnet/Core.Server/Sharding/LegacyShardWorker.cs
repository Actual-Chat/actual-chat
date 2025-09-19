using ActualLab.Diagnostics;

namespace ActualChat;

public abstract class LegacyShardWorker(IServiceProvider services, ShardScheme shardScheme, string? keyPrefix = null)
    : WorkerBase, IHasServices
{
    private static bool DebugMode => Constants.DebugMode.LegacyShardWorker;

    [field: AllowNull, MaybeNull]
    protected ILogger Log => field ??= Services.LogFor(GetType());
    protected ILogger? DebugLog => DebugMode ? Log.IfEnabled(LogLevel.Debug) : null;

    public IServiceProvider Services { get; } = services;
    public ShardDispatcher ShardDispatcher { get; } = services.ShardDispatchers()[shardScheme, keyPrefix];
    public ShardScheme ShardScheme => ShardDispatcher.ShardScheme;
    public string KeyPrefix => ShardDispatcher.KeyPrefix;

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        var runnable = new ShardRunnable(GetType().GetName(), OnRun);
        await using var _ = ShardDispatcher.Use(runnable).ConfigureAwait(false);
        await TaskExt.NeverEnding(cancellationToken).SilentAwait(false);
    }

    protected abstract Task OnRun(int shardIndex, CancellationToken cancellationToken);
}
