using ActualChat.Hosting;
using ActualChat.UI.Blazor.App.Services;
using Stl.Plugins;

namespace ActualChat.UI.Blazor.App.Module;

public class BlazorUIAppModule : HostModule, IBlazorUIModule
{
    public BlazorUIAppModule(IPluginInfoProvider.Query _) : base(_) { }
    [ServiceConstructor]
    public BlazorUIAppModule(IPluginHost plugins) : base(plugins) { }

    public override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.RequiredServiceScopes.Contains(ServiceScope.BlazorUI))
            return; // Blazor UI only module
        var isServerSideBlazor = HostInfo.RequiredServiceScopes.Contains(ServiceScope.Server);
        if (!isServerSideBlazor)
            services.AddScoped<SignOutReloader>();
    }
}
