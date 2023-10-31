using System.Diagnostics.CodeAnalysis;
using ActualChat.Contacts.Db;
using ActualChat.Db.Module;
using ActualChat.Hosting;
using ActualChat.Redis.Module;
using ActualChat.Users.Events;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework.Operations;

namespace ActualChat.Contacts.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class ContactsServiceModule(IServiceProvider moduleServices) : HostModule<ContactsSettings>(moduleServices)
{
    protected override void InjectServices(IServiceCollection services)
    {
        base.InjectServices(services);
        if (!HostInfo.AppKind.IsServer())
            return; // Server-side only module

        // Redis
        var redisModule = Host.GetModule<RedisModule>();
        redisModule.AddRedisDb<ContactsDbContext>(services, Settings.Redis);

        // DB
        var dbModule = Host.GetModule<DbModule>();
        services.AddSingleton<IDbInitializer, ContactsDbInitializer>();
        dbModule.AddDbContextServices<ContactsDbContext>(services, Settings.Db, db => {
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
            // 2. Make sure it's intact only for Stl.Fusion.Authentication + local commands
            var commandAssembly = commandType.Assembly;
            return commandAssembly == typeof(IContacts).Assembly // Contacts.Contracts assembly
                || commandType == typeof(NewUserEvent); // NewUserEvent is handled by ExternalContacts service
        });
        var fusion = services.AddFusion();

        // Module's own services
        fusion.AddService<IContacts, Contacts>();
        fusion.AddService<IContactsBackend, ContactsBackend>();
        fusion.AddService<IExternalContacts, ExternalContacts>();
        fusion.AddService<ContactLinkingJob>();
        fusion.AddService<IExternalContactsBackend, ExternalContactsBackend>();

        // Controllers, etc.
        services.AddMvcCore().AddApplicationPart(GetType().Assembly);
    }
}
