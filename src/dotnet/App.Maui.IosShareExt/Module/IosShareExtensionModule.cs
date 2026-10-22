using ActualChat.App.Maui.IosShareExt.Services;
using ActualChat.App.Maui.IosShareExt.UI.Fusion;
using ActualChat.Hosting;
using ActualChat.Rpc;
using ShareUI = ActualChat.App.Maui.IosShareExt.Services.ShareUI;

namespace ActualChat.App.Maui.IosShareExt.Module;

public sealed class IosShareExtensionModule(IServiceProvider moduleServices)
    : HostModule(moduleServices), IAppModule
{
    protected override void InjectServices(IServiceCollection services)
    {
        var fusion = services.AddFusion();
        fusion.AddIos();
        fusion.AddService<ShareUI>(ServiceLifetime.Scoped);
        services.AddScoped<ShareInputs>();
        fusion.AddService<SessionInitializer>(ServiceLifetime.Singleton);
        // One per process: its check counter keeps a check off the peer of an earlier share's check
        services.AddSingleton<IRpcPathProbe>(c => new RpcPathProbe(c));
        services.AddScoped(c => new RpcEndpointFollower(c));
    }
}
