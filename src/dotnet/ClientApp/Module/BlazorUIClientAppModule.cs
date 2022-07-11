using ActualChat.ClientApp.Services;
using ActualChat.Hosting;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Services;
using Stl.Plugins;

namespace ActualChat.ClientApp.Module;

public class BlazorUIClientAppModule : HostModule, IBlazorUIModule
{
    public BlazorUIClientAppModule(IPluginInfoProvider.Query _) : base(_) { }
    [ServiceConstructor]
    public BlazorUIClientAppModule(IPluginHost plugins) : base(plugins) { }

    public override void InjectServices(IServiceCollection services)
        => services.AddScoped<IClientAuth, MauiClientAuth>();
}
