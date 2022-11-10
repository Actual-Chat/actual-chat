using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;
using ActualChat.Kvas;
using Stl.Fusion.Client;
using Stl.Plugins;

namespace ActualChat.Users.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public class UsersClientModule : HostModule
{
    public UsersClientModule(IPluginInfoProvider.Query _) : base(_) { }
    [ServiceConstructor]
    public UsersClientModule(IPluginHost plugins) : base(plugins) { }

    public override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.RequiredServiceScopes.Contains(ServiceScope.Client))
            return; // Client-side only module

        var fusion = services.AddFusion();
        var fusionClient = services.AddFusion().AddRestEaseClient();
        var fusionAuth = fusion.AddAuthentication().AddRestEaseClient();

        fusionClient.AddReplicaService<IServerKvas, IServerKvasClientDef>();
        fusionClient.AddReplicaService<IAccounts, IAccountsClientDef>();
        fusionClient.AddReplicaService<IAvatars, IAvatarsClientDef>();
        fusionClient.AddReplicaService<IUserPresences, IUserPresencesClientDef>();
        fusionClient.AddReplicaService<IReadPositions, IReadPositionsClientDef>();
    }
}
