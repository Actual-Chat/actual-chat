using ActualChat.App.Maui.Recording;
using ActualChat.App.Maui.Services.Recording;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.Services;
using Microsoft.Maui.LifecycleEvents;

namespace ActualChat.App.Maui;

public static partial class MauiProgram
{
    private static partial void ConfigureBlazorWebViewAppPlatformServices(this IServiceCollection services)
    {
        services.AddTransient<IAppIconBadge>(_ => new WindowsAppIconBadge());
        services.AddTransient<IDeviceTokenRetriever>(_ => new WindowsDeviceTokenRetriever());
        services.AddScoped<INotificationsPermission>(_ => new WindowsNotificationsPermission());
        services.AddTransient<INativeAppSettings>(_ => new WindowsAppSettings());
        services.AddScoped<IRecordingPermissionRequester>(_ => new WindowsRecordingPermissionRequester());
        services.AddScoped<IMauiLogAccessor>(c => new WindowsLogAccessor(c.LogFor<WindowsLogAccessor>()));
        services.AddScoped<IAudioCapture>(c => new WindowsAudioCapture(c.LogFor<WindowsAudioCapture>()));
    }

    private static partial void ConfigurePlatformLifecycleEvents(ILifecycleBuilder events)
    {
        events.AddWindows(builder => {
            builder
                .OnWindowCreated(WindowConfigurator.Configure)
                .OnVisibilityChanged((_, args) => {
                    MauiBackgroundStateTracker.SetBackgroundState(!args.Visible);
                });
        });
        #if false
        // NOTE(DF): MauiLivenessProbe is switched off for now.
        WindowsLivenessProbe.Activate();
        #endif
    }
}
