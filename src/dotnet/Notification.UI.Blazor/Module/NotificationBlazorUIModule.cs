using ActualChat.Hosting;
using Stl.Plugins;

namespace ActualChat.Notification.UI.Blazor.Module;

public class NotificationBlazorUIModule: HostModule, IBlazorUIModule
{
    public NotificationBlazorUIModule(IPluginInfoProvider.Query _) : base(_) { }
    [ServiceConstructor]
    public NotificationBlazorUIModule(IPluginHost plugins) : base(plugins) { }

    public override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.RequiredServiceScopes.Contains(ServiceScope.BlazorUI))
            return; // Blazor UI only module

        var fusion = services.AddFusion();

        // Scoped / Blazor Circuit services
        fusion.AddComputeService<DeviceInfo>(ServiceLifetime.Scoped);
    }
}
