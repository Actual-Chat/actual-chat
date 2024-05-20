using System.Diagnostics.CodeAnalysis;
using ActualChat.Contacts.Db;
using ActualChat.Db.Module;
using ActualChat.Hosting;
using ActualChat.Redis.Module;
using Microsoft.EntityFrameworkCore;

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
