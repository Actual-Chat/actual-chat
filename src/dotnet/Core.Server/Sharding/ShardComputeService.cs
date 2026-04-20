using ActualLab.Diagnostics;

namespace ActualChat.Sharding;

public abstract class ShardComputeService(IServiceProvider services, ShardScheme shardScheme)
{
    protected ShardOwner ShardOwner { get; } = services.ShardOwner(shardScheme);
    protected ShardScheme ShardScheme => ShardOwner.ShardScheme;
    protected MeshWatcher MeshWatcher => ShardOwner.Host.MeshWatcher;
    protected MomentClockSet Clocks => field ??= Services.Clocks();
    protected ILogger Log => field ??= Services.LogFor(GetType());
    protected ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug);

    public IServiceProvider Services { get; } = services;
}
