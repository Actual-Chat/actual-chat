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
public sealed class ContactsServiceModule(IServiceProvider moduleServices) : HostModule(moduleServices)
{
    protected override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.HostKind.IsServer())
            return; // Server-side only module

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
            db.AddEntityResolver<string, DbExternalContact>(_ => new () {
                QueryTransformer = query => query.Include(a => a.ExternalContactLinks),
            });
        });

        // Commander & Fusion
        var commander = services.AddCommander();
        commander.AddHandlerFilter((handler, commandType) => {
            // 1. Check if this is DbOperationScopeProvider<UsersDbContext> handler
            if (handler is not InterfaceCommandHandler<ICommand> ich)
                return true;
            if (ich.ServiceType != typeof(DbOperationScopeProvider<ContactsDbContext>))
                return true;

            // 2. Make sure it's intact only for local commands
            var commandNamespace = commandType.Namespace;
            return commandNamespace.OrdinalStartsWith(typeof(IContacts).Namespace!)
                || commandType == typeof(NewUserEvent); // Event
        });
        var fusion = services.AddFusion();

        // Module's own services
        fusion.AddService<IContacts, Contacts>();
        fusion.AddService<IContactsBackend, ContactsBackend>();
        fusion.AddService<IExternalContacts, ExternalContacts>();
        fusion.AddService<IExternalContactsBackend, ExternalContactsBackend>();

        // Controllers, etc.
        services.AddMvcCore().AddApplicationPart(GetType().Assembly);
        services.AddSingleton<ContactLinker>().AddHostedService(c => c.GetRequiredService<ContactLinker>());
    }
}
