using ActualLab.Diagnostics;

namespace ActualChat;

public abstract class ShardWorker(IServiceProvider services, ShardScheme shardScheme)
    : WorkerBase, IHasServices
{
    [field: AllowNull, MaybeNull]
    protected ILogger Log => field ??= Services.LogFor(GetType());
    protected ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug);

    public IServiceProvider Services { get; } = services;
    public ShardBroker ShardBroker { get; } = services.ShardBroker(shardScheme);
    public ShardScheme ShardScheme => ShardBroker.ShardScheme;

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        await using var _ = ShardBroker.Use(GetType().GetName(), OnRun).ConfigureAwait(false);
        await TaskExt.NeverEnding(cancellationToken).SilentAwait(false);
    }

    protected abstract Task OnRun(ShardRunner runner, CancellationToken cancellationToken);
}
