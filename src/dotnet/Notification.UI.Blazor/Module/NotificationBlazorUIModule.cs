using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;

namespace ActualChat.Notification.UI.Blazor.Module;

#pragma warning disable IL2026 // Fine for modules

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class NotificationBlazorUIModule: HostModule, IBlazorUIModule
{
    public static string ImportName => "notification";

    public NotificationBlazorUIModule(IServiceProvider moduleServices) : base(moduleServices) { }

    protected override void InjectServices(IServiceCollection services)
    {
        var appKind = HostInfo.AppKind;
        if (!appKind.HasBlazorUI())
            return; // Blazor UI only module

        var fusion = services.AddFusion();

        // Scoped / Blazor Circuit services
        services.AddScoped<NotificationUI>();
        if (appKind.IsServer() || appKind.IsWasmApp()) {
            services.AddTransient<IDeviceTokenRetriever>(c => new WebDeviceTokenRetriever(c));
            services.AddScoped<INotificationsPermission>(c => c.GetRequiredService<NotificationUI>());
        }
    }
}
