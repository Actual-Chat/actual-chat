using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;

namespace ActualChat.Invite.Module;

#pragma warning disable IL2026 // Fine for modules

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
