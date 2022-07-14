using ActualChat.Hosting;
using ActualChat.UI.Blazor;
using Stl.Plugins;

namespace ActualChat.App.Wasm.Module;

public class BlazorUIHostModule : HostModule, IBlazorUIModule
{
    public BlazorUIHostModule(IPluginInfoProvider.Query _) : base(_) { }
    [ServiceConstructor]
    public BlazorUIHostModule(IPluginHost plugins) : base(plugins) { }

    public override void InjectServices(IServiceCollection services)
    { }
}
