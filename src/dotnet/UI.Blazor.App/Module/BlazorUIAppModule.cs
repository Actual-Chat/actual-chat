using ActualChat.Hosting;
using Stl.Plugins;

namespace ActualChat.UI.Blazor.App.Module;

public class BlazorUIAppModule : HostModule, IBlazorUIModule
{
    public BlazorUIAppModule(IPluginInfoProvider.Query _) : base(_) { }
    [ServiceConstructor]
    public BlazorUIAppModule(IPluginHost plugins) : base(plugins) { }

    public override void InjectServices(IServiceCollection services)
    { }
}
