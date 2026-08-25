using ActualChat.App.Maui.Audio;
using ActualChat.App.Maui.Services.Recording;
using ActualChat.Maui.Services;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.Services;
using Microsoft.Extensions.Localization;
using Microsoft.Maui.LifecycleEvents;

namespace ActualChat.App.Maui;

public static partial class MauiProgram
{
    private static partial void ConfigureBlazorWebViewAppPlatformServices(this IServiceCollection services)
    {
        services.AddTransient<IAppIconBadge>(c => new WindowsAppIconBadge(c.LogFor<WindowsAppIconBadge>()));
        services.AddTransient<IDeviceTokenRetriever>(_ => new WindowsDeviceTokenRetriever());
        services.AddScoped<INotificationsPermission>(_ => new WindowsNotificationsPermission());
        services.AddTransient<INativeAppSettings>(
            c => new WindowsAppSettings(c.GetRequiredService<IStringLocalizer>()));
        services.AddScoped<IRecordingPermissionRequester>(_ => new WindowsRecordingPermissionRequester());
        services.AddScoped<IMauiLogAccessor>(c => new WindowsLogAccessor(c));
        services.AddScoped<IAudioCapture>(c => new WindowsAudioCapture(c.LogFor<WindowsAudioCapture>()));
        services.AddScoped<ClipboardUI>(c => new WindowsClipboardUI(c.UIHub()));
        services.AddSingleton<Action<ThemeInfo>>(_ => MauiThemeHandler.Instance.OnThemeChanged);
    }

    private static partial void ConfigurePlatformLifecycleEvents(ILifecycleBuilder events)
    {
        events.AddWindows(builder => {
            builder
                .OnWindowCreated(WindowConfigurator.Configure)
                .OnVisibilityChanged((_, args) => MauiBackgroundState.Set(!args.Visible));
        });
        #if false
        // NOTE(DF): MauiLivenessProbe is switched off for now.
        WindowsLivenessProbe.Activate();
        #endif
    }
}
