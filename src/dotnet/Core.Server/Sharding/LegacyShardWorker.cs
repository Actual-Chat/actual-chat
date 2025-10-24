using ActualLab.Diagnostics;

namespace ActualChat.Sharding;

public abstract class LegacyShardWorker(IServiceProvider services, ShardScheme shardScheme)
    : WorkerBase, IHasServices
{
    [field: AllowNull, MaybeNull]
    protected ILogger Log => field ??= Services.LogFor(GetType());
    protected ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug);

    public IServiceProvider Services { get; } = services;
    public ShardOwner ShardOwner { get; } = services.ShardOwner(shardScheme);
    public ShardScheme ShardScheme => ShardOwner.ShardScheme;

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        await using var _ = ShardOwner.Use(GetType().GetName(), OnRun).ConfigureAwait(false);
        await TaskExt.NeverEnding(cancellationToken).SilentAwait(false);
    }

    protected abstract Task OnRun(int shardIndex, CancellationToken cancellationToken);
}
