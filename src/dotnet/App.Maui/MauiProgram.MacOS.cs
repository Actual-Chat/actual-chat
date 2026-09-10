using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Services;
using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.App.Services;
using Microsoft.Maui.LifecycleEvents;

namespace ActualChat.App.Maui;

public static partial class MauiProgram
{
    private static partial void ConfigureBlazorWebViewAppPlatformServices(this IServiceCollection services)
    {
        // The AppKit backend runs with the leanest platform service set: push notifications,
        // native auth, file saving and theming are not wired up yet, so their web/base
        // registrations stay in effect.
        services.AddTransient<IDeviceTokenRetriever>(_ => new MacDeviceTokenRetriever());
        services.AddScoped<IRecordingPermissionRequester>(_ => new WebRecordingPermissionRequester());
        services.AddScoped<INotificationsPermission>(c => new MacOSNotificationsPermission(c.AppUIHub()));
        services.AddScoped<IDeviceNotifications>(c => new MacOSDeviceNotifications(c));
        services.AddTransient<IAppIconBadge>(_ => new MacOSAppIconBadge());
        services.AddScoped<IFileSaver>(c => new MacOSFileSaver(c.UIHub()));
    }

    private static partial void ConfigurePlatformLifecycleEvents(ILifecycleBuilder events)
        => events.AddMacOS(macOS => macOS
            .DidFinishLaunching(_ => WindowConfigurator.Configure())
            .DidBecomeActive(_ => WindowConfigurator.UpdateBackgroundState())
            .DidResignActive(_ => WindowConfigurator.UpdateBackgroundState()));
}
