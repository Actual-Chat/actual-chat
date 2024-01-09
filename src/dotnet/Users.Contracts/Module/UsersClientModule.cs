using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;
using ActualChat.Kvas;
using ActualChat.Security;
using ActualLab.RestEase;

namespace ActualChat.Users.Module;

#pragma warning disable IL2026 // Fine for modules

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class UsersClientModule(IServiceProvider moduleServices) : HostModule(moduleServices)
{
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
        fusion.AddClient<IPhoneAuth>();
    }
}
