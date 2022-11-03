using ActualChat.Hosting;
using Stl.Fusion.Client;
using Stl.Plugins;

namespace ActualChat.Chat.Module;

public class ChatClientModule : HostModule
{
    public ChatClientModule(IPluginInfoProvider.Query _) : base(_) { }

    [ServiceConstructor]
    public ChatClientModule(IPluginHost plugins) : base(plugins) { }

    public override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.RequiredServiceScopes.Contains(ServiceScope.Client))
            return; // Client-side only module

        var fusionClient = services.AddFusion().AddRestEaseClient();
        fusionClient.AddReplicaService<IChats, IChatsClientDef>();
        fusionClient.AddReplicaService<IAuthors, IAuthorsClientDef>();
        fusionClient.AddReplicaService<IRoles, IRolesClientDef>();
        fusionClient.AddReplicaService<IMentions, IMentionsClientDef>();
        fusionClient.AddReplicaService<IReactions, IReactionsClientDef>();
    }
}
