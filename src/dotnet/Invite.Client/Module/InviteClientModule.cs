using ActualChat.Hosting;
using Stl.Fusion.Client;
using Stl.Plugins;

namespace ActualChat.Invite.Module;

public class InviteClientModule : HostModule
{
    public InviteClientModule(IPluginInfoProvider.Query _) : base(_) { }

    [ServiceConstructor]
    public InviteClientModule(IPluginHost plugins) : base(plugins) { }

    public override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.RequiredServiceScopes.Contains(ServiceScope.Client))
            return; // Client-side only module

        var fusionClient = services.AddFusion().AddRestEaseClient();
        fusionClient.AddReplicaService<IInvites, IInvitesClientDef>();
    }
}
