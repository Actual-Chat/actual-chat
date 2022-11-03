using ActualChat.Hosting;
using Stl.Fusion.Client;
using Stl.Plugins;

namespace ActualChat.Feedback.Module;

public class FeedbackClientModule : HostModule
{
    public FeedbackClientModule(IPluginInfoProvider.Query _) : base(_) { }
    [ServiceConstructor]
    public FeedbackClientModule(IPluginHost plugins) : base(plugins) { }

    public override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.RequiredServiceScopes.Contains(ServiceScope.Client))
            return; // Client-side only module

        var fusionClient = services.AddFusion().AddRestEaseClient();
        fusionClient.AddReplicaService<IFeedbacks, IFeedbacksClientDef>();
    }
}
