using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;

namespace ActualChat.Notification.UI.Blazor.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class NotificationBlazorUIModule: HostModule, IBlazorUIModule
{
    public static string ImportName => "notification";

    public NotificationBlazorUIModule(IServiceProvider moduleServices) : base(moduleServices) { }

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
        services.AddTransient<IDeviceTokenRetriever>(c => new WebDeviceTokenRetriever(c.GetRequiredService<IJSRuntime>()));
        services.AddScoped<INotificationPermissions>(c => c.GetRequiredService<NotificationUI>());
    }
}
