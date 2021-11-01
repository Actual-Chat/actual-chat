using ActualChat.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Stl.DependencyInjection;
using Stl.Fusion.Client;
using Stl.Plugins;

namespace ActualChat.Chat.Client;

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
        fusionClient.AddReplicaService<IChatServiceFacade, IChatServiceFacadeDef>();
        fusionClient.AddReplicaService<IAuthorServiceFacade, IAuthorServiceFacadeDef>();

        services.AddSingleton<IChatMediaResolver, BuiltInChatMediaResolver>();
    }
}
