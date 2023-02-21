using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Stl.Plugins;

namespace ActualChat.Notification.UI.Blazor.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public class NotificationBlazorUIModule: HostModule, IBlazorUIModule
{
    public static string ImportName => "notification";

    public NotificationBlazorUIModule(IPluginInfoProvider.Query _) : base(_) { }
    [ServiceConstructor]
    public NotificationBlazorUIModule(IPluginHost plugins) : base(plugins) { }

    public override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.AppKind.HasBlazorUI())
            return; // Blazor UI only module

        var fusion = services.AddFusion();

        // Scoped / Blazor Circuit services
        fusion.AddComputeService<NotificationUI>(ServiceLifetime.Scoped);

        if (HostInfo.AppKind == AppKind.MauiApp)
            return;

        // Web application (or WASM) services
        services.TryAddTransient<IDeviceTokenRetriever, WebDeviceTokenRetriever>();
        services.TryAddScoped<INotificationPermissions>(s => s.GetRequiredService<NotificationUI>());
    }
}
