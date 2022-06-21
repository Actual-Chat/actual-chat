using ActualChat.Hosting;
using ActualChat.UI.Blazor;
using Stl.Plugins;

namespace ActualChat.ClientApp.Module;

public class BlazorUIClientAppModule : HostModule, IBlazorUIModule
{
    public BlazorUIClientAppModule(IPluginInfoProvider.Query _) : base(_) { }
    [ServiceConstructor]
    public BlazorUIClientAppModule(IPluginHost plugins) : base(plugins) { }

    public override void InjectServices(IServiceCollection services)
    {
    }
}
