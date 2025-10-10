using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Sharding;

public abstract class ShardedDbServiceBase<TDbContext> : DbServiceBase<TDbContext>
    where TDbContext : DbContext
{
    protected ShardOwner ShardOwner { get; }
    protected ShardScheme ShardScheme => ShardOwner.ShardScheme;

    protected ShardedDbServiceBase(IServiceProvider services, ShardScheme? shardScheme = null)
        : base(services)
    {
        shardScheme ??= services.BackendServiceDefs()[GetType()].ShardScheme;
        ShardOwner = services.ShardOwner(shardScheme);
    }
}
