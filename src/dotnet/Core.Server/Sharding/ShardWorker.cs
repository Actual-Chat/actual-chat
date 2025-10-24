using ActualLab.Diagnostics;

namespace ActualChat.Sharding;

public abstract class ShardWorker(IServiceProvider services, ShardScheme shardScheme)
    : WorkerBase, IHasServices
{
    protected ShardOwner ShardOwner { get; } = services.ShardOwner(shardScheme);
    protected ShardScheme ShardScheme => ShardOwner.ShardScheme;
    [field: AllowNull, MaybeNull]
    protected ILogger Log => field ??= Services.LogFor(GetType());
    protected ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug);

    public IServiceProvider Services { get; } = services;

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        await using var _ = ShardOwner.Use(GetType().GetName(), OnRun).ConfigureAwait(false);
        await TaskExt.NeverEnding(cancellationToken).SilentAwait(false);
    }

    protected abstract Task OnRun(ShardOwnership shardOwnership, CancellationToken cancellationToken);
}
