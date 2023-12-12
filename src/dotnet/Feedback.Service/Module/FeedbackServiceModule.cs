using System.Diagnostics.CodeAnalysis;
using ActualChat.Db.Module;
using ActualChat.Feedback.Db;
using ActualChat.Hosting;
using ActualChat.Redis.Module;
using Stl.Fusion.EntityFramework.Operations;

namespace ActualChat.Feedback.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class FeedbackServiceModule(IServiceProvider moduleServices)
    : HostModule<FeedbackSettings>(moduleServices)
{
    protected override void InjectServices(IServiceCollection services)
    {
        base.InjectServices(services);
        if (!HostInfo.AppKind.IsServer())
            return; // Server-side only module

        // Redis
        var redisModule = Host.GetModule<RedisModule>();
        redisModule.AddRedisDb<FeedbackDbContext>(services, Settings.Redis);

        // DB
        var dbModule = Host.GetModule<DbModule>();
        services.AddSingleton<IDbInitializer, FeedbackDbInitializer>();
        dbModule.AddDbContextServices<FeedbackDbContext>(services, Settings.Db);

        // Commander & Fusion
        var commander = services.AddCommander();
        commander.AddHandlerFilter((handler, commandType) => {
            // 1. Check if this is DbOperationScopeProvider<FeedbackDbContext> handler
            if (handler is not InterfaceCommandHandler<ICommand> ich)
                return true;
            if (ich.ServiceType != typeof(DbOperationScopeProvider<FeedbackDbContext>))
                return true;
            // 2. Make sure it's intact only for local commands
            var commandAssembly = commandType.Assembly;
            if (commandAssembly == typeof(IFeedbacks).Assembly) // Feedback.Contracts assembly
                return true;
            return false;
        });
        var fusion = services.AddFusion();

        // Module's own services
        fusion.AddService<IFeedbacks, Feedbacks>();

        // Controllers, etc.
        services.AddMvcCore().AddApplicationPart(GetType().Assembly);
    }
}
