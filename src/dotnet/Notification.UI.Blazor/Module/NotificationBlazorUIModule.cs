using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;

namespace ActualChat.Notification.UI.Blazor.Module;

#pragma warning disable IL2026 // Fine for modules

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class NotificationBlazorUIModule(IServiceProvider moduleServices)
    : HostModule(moduleServices), IBlazorUIModule
{
    public static string ImportName => "notification";

    protected override void InjectServices(IServiceCollection services)
    {
        services.AddFusion();
        services.AddScoped<NotificationUI>();
        if (HostInfo.HostKind.IsServerOrWasmApp()) {
            services.AddTransient<IDeviceTokenRetriever>(c => new WebDeviceTokenRetriever(c));
            services.AddScoped<INotificationsPermission>(c => c.GetRequiredService<NotificationUI>());
        }
    }
}
