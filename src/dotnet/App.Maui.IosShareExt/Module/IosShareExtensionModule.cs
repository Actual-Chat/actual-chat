using ActualChat.App.Maui.IosShareExt.Services;
using ActualChat.App.Maui.IosShareExt.UI.Fusion;
using ActualChat.Hosting;
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
    }
}
