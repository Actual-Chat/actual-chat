using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;
using ActualChat.Kvas;
using ActualChat.Security;
using Stl.RestEase;

namespace ActualChat.Users.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class UsersClientModule : HostModule
{
    public UsersClientModule(IServiceProvider moduleServices) : base(moduleServices) { }

    protected override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.AppKind.IsClient())
            return; // Client-side only module

        if (HostInfo.AppKind.IsMauiApp())
            services.AddRestEase(restEase => restEase.AddClient<INativeAuthClient>());

        var fusion = services.AddFusion().AddAuthClient();
        var rpc = fusion.Rpc;
        rpc.AddClient<ISecureTokens>();

        fusion.AddClient<ISystemProperties>();
        fusion.AddClient<IMobileSessions>();
        fusion.AddClient<IServerKvas>();
        fusion.AddClient<IAccounts>();
        fusion.AddClient<IAvatars>();
        fusion.AddClient<IUserPresences>();
        fusion.AddClient<IChatPositions>();
    }
}
