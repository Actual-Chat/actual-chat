using System.Diagnostics.CodeAnalysis;
using ActualChat.Contacts.Db;
using ActualChat.Db.Module;
using ActualChat.Hosting;
using ActualChat.Redis.Module;
using Stl.Fusion.EntityFramework.Operations;

namespace ActualChat.Contacts.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class ContactsServiceModule : HostModule<ContactsSettings>
{
    public ContactsServiceModule(IServiceProvider moduleServices) : base(moduleServices) { }

    public static HttpMessageHandler? GoogleBackchannelHttpHandler { get; set; }

    protected override void InjectServices(IServiceCollection services)
    {
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
            if (commandAssembly == typeof(IContacts).Assembly) // Contacts.Contracts assembly
                return true;
            return false;
        });
        var fusion = services.AddFusion();

        // Module's own services
        fusion.AddService<IContacts, Contacts>();
        fusion.AddService<IContactsBackend, ContactsBackend>();

        // Controllers, etc.
        services.AddMvcCore().AddApplicationPart(GetType().Assembly);
    }
}
