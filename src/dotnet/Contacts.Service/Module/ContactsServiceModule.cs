using ActualChat.Contacts.Db;
using ActualChat.Db.Module;
using ActualChat.Hosting;
using ActualChat.Redis.Module;
using Stl.Fusion.EntityFramework.Operations;
using Stl.Plugins;

namespace ActualChat.Contacts.Module;

public class ContactsServiceModule : HostModule<ContactsSettings>
{
    public ContactsServiceModule(IPluginInfoProvider.Query _) : base(_) { }
    [ServiceConstructor]
    public ContactsServiceModule(IPluginHost plugins) : base(plugins) { }

    public static HttpMessageHandler? GoogleBackchannelHttpHandler { get; set; }

    public override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.RequiredServiceScopes.Contains(ServiceScope.Server))
            return; // Server-side only module

        // Redis
        var redisModule = Plugins.GetPlugins<RedisModule>().Single();
        redisModule.AddRedisDb<ContactsDbContext>(services, Settings.Redis);

        // DB
        var dbModule = Plugins.GetPlugins<DbModule>().Single();
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
        fusion.AddComputeService<IContacts, Contacts>();
        fusion.AddComputeService<IContactsBackend, ContactsBackend>();

        // Controllers, etc.
        services.AddMvcCore().AddApplicationPart(GetType().Assembly);
    }
}
