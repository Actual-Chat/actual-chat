using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;

namespace ActualChat.Feedback.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class FeedbackClientModule : HostModule
{
    public FeedbackClientModule(IServiceProvider moduleServices) : base(moduleServices) { }

    protected override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.AppKind.IsClient())
            return; // Client-side only module

        var fusion = services.AddFusion();
        fusion.AddClient<IFeedbacks>();
    }
}
