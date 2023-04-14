using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ActualChat.Notification.UI.Blazor.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class NotificationBlazorUIModule: HostModule, IBlazorUIModule
{
    public static string ImportName => "notification";

    public NotificationBlazorUIModule(IServiceProvider services) : base(services) { }

    protected override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.AppKind.HasBlazorUI())
            return; // Blazor UI only module

        var fusion = services.AddFusion();

        // Scoped / Blazor Circuit services
        services.AddScoped<NotificationUI>();

        if (HostInfo.AppKind == AppKind.MauiApp)
            return;

        // Web application (or WASM) services
        services.TryAddTransient<IDeviceTokenRetriever, WebDeviceTokenRetriever>();
        services.TryAddScoped<INotificationPermissions>(s => s.GetRequiredService<NotificationUI>());
    }
}
