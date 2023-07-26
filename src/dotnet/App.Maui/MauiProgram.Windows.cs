using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Notification.UI.Blazor;
using ActualChat.UI.Blazor.App.Services;
using Microsoft.Maui.Controls.PlatformConfiguration;
using Microsoft.Maui.LifecycleEvents;
using Serilog;
using Stl.IO;

namespace ActualChat.App.Maui;

public static partial class MauiProgram
{
    private static partial void AddPlatformServices(this IServiceCollection services)
    {
        services.AddTransient<IAppIconBadge>(_ => new WindowsAppIconBadge());
        services.AddTransient<IDeviceTokenRetriever>(_ => new WindowsDeviceTokenRetriever());
        services.AddScoped<INotificationPermissions>(_ => new WindowsNotificationPermissions());
        services.AddTransient<INativeAppSettings>(_ => new WindowsAppSettings());
    }

    private static partial void AddPlatformServicesToSkip(HashSet<Type> servicesToSkip)
    { }

    private static partial void ConfigurePlatformLifecycleEvents(ILifecycleBuilder events)
        => events.AddWindows(builder => {
            builder.OnWindowCreated(WindowsMinimization.Configure);
        });
}
