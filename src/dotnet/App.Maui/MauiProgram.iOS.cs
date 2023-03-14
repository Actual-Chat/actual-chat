using ActualChat.Notification.UI.Blazor;
using Microsoft.Maui.LifecycleEvents;
using Serilog;

namespace ActualChat.App.Maui;

public static partial class MauiProgram
{
    private static partial LoggerConfiguration ConfigurePlatformLogger(this LoggerConfiguration loggerConfiguration)
        => loggerConfiguration.WriteTo.NSLog();

    private static partial void AddPlatformServices(this IServiceCollection services)
    {
        services.AddTransient<IDeviceTokenRetriever, IosDeviceTokenRetriever>(c => new IosDeviceTokenRetriever(c));
        services.AddScoped<INotificationPermissions>(c => new IosNotificationPermissions());
    }

    private static partial void ConfigurePlatformLifecycleEvents(ILifecycleBuilder events)
    { }
}
