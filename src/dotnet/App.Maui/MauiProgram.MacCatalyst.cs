using ActualChat.Notification.UI.Blazor;
using Microsoft.Maui.LifecycleEvents;
using Serilog;

namespace ActualChat.App.Maui;

public static partial class MauiProgram
{
    public static partial LoggerConfiguration ConfigurePlatformLogger(LoggerConfiguration loggerConfiguration)
        => loggerConfiguration;

    public static partial string? GetAppSettingsFilePath()
        => null;

    private static partial void AddPlatformServices(this IServiceCollection services)
    {
        services.AddTransient<IDeviceTokenRetriever>(_ => new MacDeviceTokenRetriever());
        services.AddScoped<INotificationPermissions>(_ => new MacNotificationPermissions());
    }

    private static partial void AddPlatformServicesToSkip(HashSet<Type> servicesToSkip)
    { }

    private static partial void ConfigurePlatformLifecycleEvents(ILifecycleBuilder events)
    { }
}
