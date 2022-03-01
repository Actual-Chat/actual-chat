using ActualChat.Db.Module;
using ActualChat.Feedback.Db;
using ActualChat.Hosting;
using ActualChat.Redis.Module;
using Stl.Fusion.EntityFramework.Operations;
using Stl.Plugins;

namespace ActualChat.Feedback.Module;

public class FeedbackModule : HostModule<FeedbackSettings>
{
    public FeedbackModule(IPluginInfoProvider.Query _) : base(_)
    {
    }

    [ServiceConstructor]
    public FeedbackModule(IPluginHost plugins) : base(plugins)
    {
    }

    public override void InjectServices(IServiceCollection services)
    {
        base.InjectServices(services);
        if (!HostInfo.RequiredServiceScopes.Contains(ServiceScope.Server))
            return; // Server-side only module

        // Redis
        var redisModule = Plugins.GetPlugins<RedisModule>().Single();
        redisModule.AddRedisDb<FeedbackDbContext>(services, Settings.Redis);

        // DB
        var dbModule = Plugins.GetPlugins<DbModule>().Single();
        dbModule.AddDbContextServices<FeedbackDbContext>(services, Settings.Db);
        services.AddSingleton<IDbInitializer, FeedbackDbInitializer>();

        // Fusion services
        var fusion = services.AddFusion();
        services.AddCommander().AddHandlerFilter((handler, commandType) => {
            // 1. Check if this is DbOperationScopeProvider<UsersDbContext> handler
            if (handler is not InterfaceCommandHandler<ICommand> ich)
                return true;
            if (ich.ServiceType != typeof(DbOperationScopeProvider<FeedbackDbContext>))
                return true;
            // 2. Make sure it's intact only for Stl.Fusion.Authentication + local commands
            var commandAssembly = commandType.Assembly;
            if (commandAssembly == typeof(IFeedback.FeatureRequestCommand).Assembly)
                return true;
            return false;
        });

        fusion.AddComputeService<IFeedback, FeedbackService>();
    }
}
