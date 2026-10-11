using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using Microsoft.Maui.LifecycleEvents;

namespace ActualChat.App.Maui;

public static partial class MauiProgram
{
    private static partial void ConfigureBlazorWebViewAppPlatformServices(this IServiceCollection services)
    {
        // The GTK backend runs with the leanest platform service set, like the AppKit one did at
        // first: whatever has a web implementation - file picking and saving, the clipboard,
        // theming - keeps it, since WebKitGTK serves all of that from the page.
        services.AddTransient<IDeviceTokenRetriever>(_ => new NoDeviceTokenRetriever());
        services.AddScoped<INotificationsPermission>(_ => new LinuxNotificationsPermission());
        services.AddScoped<IDeviceNotifications>(c => new LinuxDeviceNotifications(c));
        services.AddScoped<IRecordingPermissionRequester>(_ => new WebRecordingPermissionRequester());
        // The page's origin is app://localhost, so WebAuthn can't target the RP id
        services.AddScoped<IPasskeyClient>(_ => new UnavailablePasskeyClient());
    }

    private static partial void ConfigurePlatformLifecycleEvents(ILifecycleBuilder events)
    { }
}
