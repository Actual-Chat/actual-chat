using System.Diagnostics.CodeAnalysis;
using System.Net;
using ActualChat.Hosting;
using Stl.Fusion.Client;
using Stl.Plugins;

namespace ActualChat.Chat.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public class ChatClientModule : HostModule
{
    public ChatClientModule(IPluginInfoProvider.Query _) : base(_) { }

    [ServiceConstructor]
    public ChatClientModule(IPluginHost plugins) : base(plugins) { }

    public override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.AppKind.IsClient())
            return; // Client-side only module

        var fusionClient = services.AddFusion().AddRestEaseClient();
        fusionClient.ConfigureHttpClient((c, name, o) => {
            o.HttpClientActions.Add(client => {
                client.DefaultRequestVersion = HttpVersion.Version30;
                client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
            });
        });
        fusionClient.AddReplicaService<IChats, IChatsClientDef>();
        fusionClient.AddReplicaService<IAuthors, IAuthorsClientDef>();
        fusionClient.AddReplicaService<IRoles, IRolesClientDef>();
        fusionClient.AddReplicaService<IMentions, IMentionsClientDef>();
        fusionClient.AddReplicaService<IReactions, IReactionsClientDef>();
    }
}
