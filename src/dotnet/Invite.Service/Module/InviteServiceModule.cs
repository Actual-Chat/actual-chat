using System.Diagnostics.CodeAnalysis;
using ActualChat.Db.Module;
using ActualChat.Hosting;
using ActualChat.Invite.Backend;
using ActualChat.Invite.Db;
using ActualChat.Redis.Module;
using Stl.Fusion.EntityFramework.Operations;

namespace ActualChat.Invite.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class InviteServiceModule : HostModule<InviteSettings>
{
    public InviteServiceModule(IServiceProvider services) : base(services) { }

    protected override void InjectServices(IServiceCollection services)
    {
        base.InjectServices(services);
        if (!HostInfo.AppKind.IsServer())
            return; // Server-side only module

        // Redis
        var redisModule = Host.GetModule<RedisModule>();
        redisModule.AddRedisDb<InviteDbContext>(services, Settings.Redis);

        // DB
        var dbModule = Host.GetModule<DbModule>();
        services.AddSingleton<IDbInitializer, InviteDbInitializer>();
        dbModule.AddDbContextServices<InviteDbContext>(services, Settings.Db, db => {
            // DbInvite
            db.AddEntityResolver<string, DbInvite>();
            db.AddEntityResolver<string, DbActivationKey>();
        });

        // Commander & Fusion
        var commander = services.AddCommander();
        commander.AddHandlerFilter((handler, commandType) => {
            // 1. Check if this is DbOperationScopeProvider<InviteDbContext> handler
            if (handler is not InterfaceCommandHandler<ICommand> ich)
                return true;
            if (ich.ServiceType != typeof(DbOperationScopeProvider<InviteDbContext>))
                return true;
            // 2. Make sure it's intact only for local commands
            var commandAssembly = commandType.Assembly;
            if (commandAssembly == typeof(IInvites).Assembly) // Invite.Contracts assembly
                return true;
            return false;
        });
        var fusion = services.AddFusion();

        // Module's own services
        fusion.AddService<IInvites, Invites>();
        fusion.AddService<IInvitesBackend, InvitesBackend>();
        // services.AddSingleton<ITextSerializer>(SystemJsonSerializer.Default);

        // Controllers, etc.
        services.AddMvcCore().AddApplicationPart(GetType().Assembly);
    }
}
