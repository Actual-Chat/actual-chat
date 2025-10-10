using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Sharding;

public abstract class ShardedDbWorkerBase<TDbContext> : DbWorkerBase<TDbContext>
    where TDbContext : DbContext
{
    protected ShardOwner ShardOwner { get; }
    protected ShardScheme ShardScheme => ShardOwner.ShardScheme;

    protected ShardedDbWorkerBase(IServiceProvider services, ShardScheme? shardScheme = null)
        : base(services)
    {
        shardScheme ??= services.BackendServiceDefs()[GetType().NonProxyType()].ShardScheme;
        ShardOwner = services.ShardOwner(shardScheme);
    }
}
