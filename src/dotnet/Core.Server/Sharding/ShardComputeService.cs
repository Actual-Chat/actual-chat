using ActualLab.Diagnostics;

namespace ActualChat.Sharding;

public abstract class ShardComputeService(IServiceProvider services, ShardScheme shardScheme)
{
    protected ShardBroker ShardBroker { get; } = services.ShardBroker(shardScheme);
    protected ShardScheme ShardScheme => ShardBroker.ShardScheme;
    [field: AllowNull, MaybeNull]
    protected ILogger Log => field ??= Services.LogFor(GetType());
    protected ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug);

    public IServiceProvider Services { get; } = services;
}
