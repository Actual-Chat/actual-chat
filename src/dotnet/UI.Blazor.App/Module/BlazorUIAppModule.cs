using ActualChat.Hosting;
using ActualChat.UI.Blazor.App.Components;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Stl.Plugins;

namespace ActualChat.UI.Blazor.App.Module;

public class BlazorUIAppModule : HostModule, IBlazorUIModule
{
    public BlazorUIAppModule(IPluginInfoProvider.Query _) : base(_) { }
    [ServiceConstructor]
    public BlazorUIAppModule(IPluginHost plugins) : base(plugins) { }

    public override void InjectServices(IServiceCollection services)
    {
        // Host-specific services
        services.TryAddSingleton(new WelcomeOptions());
    }
}
