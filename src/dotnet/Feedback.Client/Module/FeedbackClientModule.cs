using System.Diagnostics.CodeAnalysis;
using System.Net;
using ActualChat.Hosting;
using Stl.Fusion.Client;
using Stl.Plugins;

namespace ActualChat.Feedback.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public class FeedbackClientModule : HostModule
{
    public FeedbackClientModule(IPluginInfoProvider.Query _) : base(_) { }
    [ServiceConstructor]
    public FeedbackClientModule(IPluginHost plugins) : base(plugins) { }

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
        fusionClient.AddReplicaService<IFeedbacks, IFeedbacksClientDef>();
    }
}
