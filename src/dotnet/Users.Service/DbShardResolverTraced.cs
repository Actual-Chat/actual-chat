using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Users;

internal class DbShardResolverTraced<TDbContext> (IServiceProvider services) : DbShardResolver<TDbContext>(services) {

    public override DbShard Resolve(object source) {
        using var _activity = AppDiagnostics.AppTrace.StartActivity("ShardResolver:Resolve");
        return base.Resolve(source);
    }

}