using ActualLab.Diagnostics;

namespace ActualChat.Sharding;

public abstract class ShardWorker(IServiceProvider services, ShardScheme shardScheme)
    : WorkerBase, IHasServices
{
    protected ShardBroker ShardBroker { get; } = services.ShardBroker(shardScheme);
    protected ShardScheme ShardScheme => ShardBroker.ShardScheme;
    [field: AllowNull, MaybeNull]
    protected ILogger Log => field ??= Services.LogFor(GetType());
    protected ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug);

    public IServiceProvider Services { get; } = services;

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        await using var _ = ShardBroker.Use(GetType().GetName(), OnRun).ConfigureAwait(false);
        await TaskExt.NeverEnding(cancellationToken).SilentAwait(false);
    }

    protected abstract Task OnRun(ShardLease shardLease, CancellationToken cancellationToken);
}
