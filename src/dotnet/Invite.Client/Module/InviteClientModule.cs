using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;

namespace ActualChat.Invite.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class InviteClientModule : HostModule
{
    public InviteClientModule(IServiceProvider moduleServices) : base(moduleServices) { }

    protected override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.AppKind.IsClient())
            return; // Client-side only module

        var fusion = services.AddFusion();
        fusion.AddClient<IInvites>();
    }
}
