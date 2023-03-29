using System.Diagnostics.CodeAnalysis;
using System.Net;
using ActualChat.Hosting;
using ActualChat.Kvas;
using Stl.Fusion.Client;
using Stl.Interception;
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
        if (!HostInfo.AppKind.IsClient())
            return; // Client-side only module

        var fusion = services.AddFusion();
        var fusionClient = services.AddFusion().AddRestEaseClient();
        fusionClient.ConfigureHttpClient((c, name, o) => {
            o.HttpClientActions.Add(client => {
                client.DefaultRequestVersion = HttpVersion.Version30;
                client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
            });
        });
        var fusionAuth = fusion.AddAuthentication().AddRestEaseClient();

        fusionClient.AddReplicaService<ISystemProperties, ISystemPropertiesClientDef>();
        fusionClient.AddReplicaService<IServerKvas, IServerKvasClientDef>();
        fusionClient.AddReplicaService<IAccounts, IAccountsClientDef>();
        fusionClient.AddReplicaService<IAvatars, IAvatarsClientDef>();
        fusionClient.AddReplicaService<IUserPresences, IUserPresencesClientDef>();
        fusionClient.AddReplicaService<IChatPositions, IChatPositionsClientDef>();
    }
}
