using ActualChat.Hosting;
using Stl.Plugins;

namespace ActualChat.UI.Blazor.Host.Module;

public class BlazorUIHostModule : HostModule, IBlazorUIModule
{
    public BlazorUIHostModule(IPluginInfoProvider.Query _) : base(_) { }
    [ServiceConstructor]
    public BlazorUIHostModule(IPluginHost plugins) : base(plugins) { }

    public override void InjectServices(IServiceCollection services)
    { }
}
