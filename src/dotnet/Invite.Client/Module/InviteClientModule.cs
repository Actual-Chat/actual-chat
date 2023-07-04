using System.Diagnostics.CodeAnalysis;
using System.Net;
using ActualChat.Hosting;
using Stl.Fusion.Client;

namespace ActualChat.Invite.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class InviteClientModule : HostModule
{
    public InviteClientModule(IServiceProvider services) : base(services) { }

    protected override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.AppKind.IsClient())
            return; // Client-side only module

        var fusion = services.AddFusion();
        fusion.AddClient<IInvites>();
    }
}
