using System.Diagnostics.CodeAnalysis;
using System.Net;
using ActualChat.Hosting;
using ActualChat.Kvas;
using Stl.Fusion.Client;

namespace ActualChat.Users.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class UsersClientModule : HostModule
{
    public UsersClientModule(IServiceProvider services) : base(services) { }

    protected override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.AppKind.IsClient())
            return; // Client-side only module

        var fusion = services.AddFusion().AddAuthClient();

        fusion.AddClient<ISystemProperties>();
        fusion.AddClient<IMobileSessions>();
        fusion.AddClient<IServerKvas>();
        fusion.AddClient<IAccounts>();
        fusion.AddClient<IAvatars>();
        fusion.AddClient<IUserPresences>();
        fusion.AddClient<IChatPositions>();
    }
}
