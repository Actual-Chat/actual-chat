using ActualChat.App.Maui.Services;
using ActualChat.UI.App.Services;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Components;
using ActualChat.UI.Blazor.Services;
using Microsoft.Maui.LifecycleEvents;

namespace ActualChat.App.Maui;

public static partial class MauiProgram
{
    private static partial void ConfigureBlazorWebViewAppPlatformServices(this IServiceCollection services)
    {
        // Push notifications are not wired up on MacCatalyst yet (no Firebase native bindings).
        services.AddTransient<IDeviceTokenRetriever>(_ => new MacDeviceTokenRetriever());
        services.AddScoped<INotificationsPermission>(_ => new MacNotificationsPermission());
        services.AddScoped<IRecordingPermissionRequester>(_ => new AppleRecordingPermissionRequester());
        services.AddScoped(c => new NativeAppleAuth(c));
        services.AddSingleton<Action<ThemeInfo>>(_ => MauiThemeHandler.Instance.OnThemeChanged);
        services.AddScoped<IMediaSaver>(c => new AppleMediaSaver(c.UIHub()));
        services.AddScoped<ClipboardUI>(c => new MacClipboardUI(c.UIHub()));
        services.AddScoped<AddPhotoPermissionHandler>(c => new AddPhotoPermissionHandler(c.UIHub()));
        services.AddTransient<IAppIconBadge>(_ => new AppIconBadge());
    }

    private static partial void ConfigurePlatformLifecycleEvents(ILifecycleBuilder events)
    { }
}
