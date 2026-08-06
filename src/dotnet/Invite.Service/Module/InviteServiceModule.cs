using ActualChat.Db.Module;
using ActualChat.Invite.Db;
using ActualChat.Redis.Module;

namespace ActualChat.Invite.Module;

public sealed class InviteServiceModule(IServiceProvider moduleServices)
    : HostModule(moduleServices), IServerModule
{
    protected override void InjectServices(IServiceCollection services)
    {
        // RPC host
        var rpcHost = services.AddRpcHost(HostInfo);
        var isBackendClient = HostInfo.Roles.GetBackendServiceMode<IInvitesBackend>() is ServiceMode.Client;

        // Invites
        rpcHost.AddApi<IInvites, Invites>();
        rpcHost.AddBackend<IInvitesBackend, InvitesBackend>();

        if (isBackendClient)
            return;

        // The services below are used only when this module operates in non-client mode

        // Redis
        var redisModule = Host.GetModule<RedisModule>();
        redisModule.AddRedisDb<InviteDbContext>(services);

        // DB
        var dbModule = Host.GetModule<DbModule>();
        services.AddSingleton<IDbInitializer, InviteDbInitializer>();
        dbModule.AddDbContextServices<InviteDbContext>(services, db => {
            // DbInvite
            db.AddEntityResolver<string, DbInvite>();
        });
    }
}
