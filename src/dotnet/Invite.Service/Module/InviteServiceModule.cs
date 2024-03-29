using System.Diagnostics.CodeAnalysis;
using ActualChat.Db.Module;
using ActualChat.Hosting;
using ActualChat.Invite.Db;
using ActualChat.Redis.Module;
using ActualLab.Fusion.EntityFramework.Operations;

namespace ActualChat.Invite.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class InviteServiceModule(IServiceProvider moduleServices)
    : HostModule(moduleServices), IServerModule
{
    protected override void InjectServices(IServiceCollection services)
    {
        // RPC host
        var rpcHost = services.AddRpcHost(HostInfo);
        var isBackendClient = HostInfo.Roles.GetBackendServiceMode<IInvitesBackend>().IsClient();

        // Invites
        rpcHost.AddApi<IInvites, Invites>();
        rpcHost.AddBackend<IInvitesBackend, InvitesBackend>();

        // Commander handlers
        rpcHost.Commander.AddHandlerFilter((handler, commandType) => {
            // 1. Check if this is DbOperationScopeProvider<InviteDbContext> handler
            if (handler is not InterfaceCommandHandler<ICommand> ich)
                return true;
            if (ich.ServiceType != typeof(DbOperationScopeProvider<InviteDbContext>))
                return true;

            // 2. Check if we're running on the client backend
            if (isBackendClient)
                return false;

            // 3. Make sure the handler is intact only for local commands
            var commandNamespace = commandType.Namespace;
            return commandNamespace.OrdinalStartsWith(typeof(IInvites).Namespace!);
        });
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
            db.AddEntityResolver<string, DbActivationKey>();
        });
    }
}
