using ActualChat.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Stl.DependencyInjection;
using Stl.Fusion.Client;
using Stl.Plugins;

namespace ActualChat.Chat.Frontend.Client;

public class ChatFrontendClientModule : HostModule
{
    public ChatFrontendClientModule(IPluginInfoProvider.Query _) : base(_) { }

    [ServiceConstructor]
    public ChatFrontendClientModule(IPluginHost plugins) : base(plugins) { }

    public override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.RequiredServiceScopes.Contains(ServiceScope.Client))
            return; // Client-side only module

        var fusionClient = services.AddFusion().AddRestEaseClient();
        fusionClient.AddReplicaService<IChatServiceFrontend, IChatServiceFrontendDef>();
        fusionClient.AddReplicaService<IAuthorServiceFrontend, IAuthorServiceFrontendDef>();
    }
}
