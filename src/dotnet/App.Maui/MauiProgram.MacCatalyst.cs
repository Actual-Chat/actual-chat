using ActualChat.Notification.UI.Blazor;
using Microsoft.Maui.LifecycleEvents;
using Serilog;

namespace ActualChat.App.Maui;

public static partial class MauiProgram
{
    private static partial LoggerConfiguration ConfigurePlatformLogger(this LoggerConfiguration loggerConfiguration)
        => loggerConfiguration;

    private static partial void AddPlatformServices(this IServiceCollection services)
    {
        services.AddTransient<IDeviceTokenRetriever, MacDeviceTokenRetriever>(_ => new MacDeviceTokenRetriever());
        services.AddScoped<INotificationPermissions>(c => new MacNotificationPermissions());
    }

    private static partial void AddPlatformServicesToSkip(HashSet<Type> servicesToSkip)
    { }

    private static partial void ConfigurePlatformLifecycleEvents(ILifecycleBuilder events)
    { }
}
