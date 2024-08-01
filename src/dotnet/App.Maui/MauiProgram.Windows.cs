using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Notification.UI.Blazor;
using ActualChat.Streaming.UI.Blazor.Services;
using ActualChat.UI.Blazor.App.Services;
using Microsoft.Maui.LifecycleEvents;

namespace ActualChat.App.Maui;

public static partial class MauiProgram
{
    private static partial void AddPlatformServices(this IServiceCollection services)
    {
        services.AddTransient<IAppIconBadge>(_ => new WindowsAppIconBadge());
        services.AddTransient<IDeviceTokenRetriever>(_ => new WindowsDeviceTokenRetriever());
        services.AddScoped<INotificationsPermission>(_ => new WindowsNotificationsPermission());
        services.AddTransient<INativeAppSettings>(_ => new WindowsAppSettings());
        services.AddScoped<IRecordingPermissionRequester>(_ => new WindowsRecordingPermissionRequester());
        services.AddScoped<IMauiLogAccessor>(c => new WindowsLogAccessor(c.LogFor<WindowsLogAccessor>()));
    }

    private static partial void AddPlatformServicesToSkip(HashSet<Type> servicesToSkip)
    { }

    private static partial void ConfigurePlatformLifecycleEvents(ILifecycleBuilder events)
    {
        events.AddWindows(builder => {
            builder.OnWindowCreated(WindowConfigurator.Configure);
        });
        WindowsLivenessProbe.Activate();
    }
}
