using System.Diagnostics.CodeAnalysis;
using ActualChat.Contacts.Db;
using ActualChat.Db.Module;
using ActualChat.Hosting;
using ActualChat.Redis.Module;
using ActualChat.Users.Events;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework.Operations;

namespace ActualChat.Contacts.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class ContactsServiceModule(IServiceProvider moduleServices)
    : HostModule(moduleServices), IServerModule
{
    protected override void InjectServices(IServiceCollection services)
    {
        // RPC host
        var rpcHost = services.AddRpcHost(HostInfo);
        var isBackendClient = HostInfo.Roles.GetBackendServiceMode<IContactsBackend>().IsClient();

        // Contacts
        rpcHost.AddApiOrLocal<IContacts, Contacts>(); // Used by many, incl. Chats.
        rpcHost.AddBackend<IContactsBackend, ContactsBackend>();

        // External contacts
        rpcHost.AddApi<IExternalContacts, ExternalContacts>();
        rpcHost.AddApi<IExternalContactHashes, ExternalContactHashes>();
        rpcHost.AddBackend<IExternalContactsBackend, ExternalContactsBackend>();
        rpcHost.AddBackend<IExternalContactHashesBackend, ExternalContactHashesBackend>();

        // Commander handlers
        rpcHost.Commander.AddHandlerFilter((handler, commandType) => {
            // 1. Check if this is DbOperationScopeProvider<UsersDbContext> handler
            if (handler is not InterfaceCommandHandler<ICommand> ich)
                return true;
            if (ich.ServiceType != typeof(DbOperationScopeProvider<ContactsDbContext>))
                return true;

            // 2. Check if we're running on the client backend
            if (isBackendClient)
                return false;

            // 3. Make sure the handler is intact only for local commands
            var commandNamespace = commandType.Namespace;
            return commandNamespace.OrdinalStartsWith(typeof(IContacts).Namespace!)
                || commandType == typeof(NewUserEvent); // Event
        });
        if (isBackendClient)
            return;

        // The services below are used only when this module operates in non-client mode

        // Internal services
        services.AddSingleton<ContactLinker>()
            .AddHostedService(c => c.GetRequiredService<ContactLinker>());

        // Redis
        var redisModule = Host.GetModule<RedisModule>();
        redisModule.AddRedisDb<ContactsDbContext>(services);

        // DB
        var dbModule = Host.GetModule<DbModule>();
        services.AddSingleton<IDbInitializer, ContactsDbInitializer>();
        dbModule.AddDbContextServices<ContactsDbContext>(services, db => {
            // Overriding / adding extra DbAuthentication services
            db.AddEntityResolver<string, DbContact>();

            // DbExternalContact
            db.AddEntityResolver<string, DbExternalContactsHash>();
            db.AddEntityResolver<string, DbExternalContact>(_ => new () {
                QueryTransformer = query => query.Include(a => a.ExternalContactLinks),
            });
        });
    }
}
