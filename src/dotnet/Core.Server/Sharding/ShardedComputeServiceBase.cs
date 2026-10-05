using ActualLab.Diagnostics;

namespace ActualChat.Sharding;

public abstract class ShardedComputeServiceBase(IServiceProvider services, ShardScheme shardScheme)
{
    protected IServiceProvider Services { get; } = services;
    protected ShardOwner ShardOwner { get; } = services.ShardOwner(shardScheme);
    protected ShardScheme ShardScheme => ShardOwner.ShardScheme;
    protected MomentClockSet Clocks => field ??= Services.Clocks();
    protected ILogger Log => field ??= Services.LogFor(GetType());
    protected ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug);
}
