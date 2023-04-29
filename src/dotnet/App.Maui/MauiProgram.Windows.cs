using ActualChat.Notification.UI.Blazor;
using Microsoft.Maui.LifecycleEvents;
using Serilog;

namespace ActualChat.App.Maui;

public static partial class MauiProgram
{
    public static partial LoggerConfiguration ConfigurePlatformLogger(LoggerConfiguration loggerConfiguration)
        => loggerConfiguration
            .WriteTo.Debug(
                outputTemplate:"[{Timestamp:HH:mm:ss.fff} {Level:u3} ({ThreadID})] [{SourceContext}] {Message:lj}{NewLine}{Exception}");

    private static partial void AddPlatformServices(this IServiceCollection services)
    {
        services.AddTransient<IDeviceTokenRetriever>(_ => new WindowsDeviceTokenRetriever());
        services.AddScoped<INotificationPermissions>(_ => new WindowsNotificationPermissions());
    }

    private static partial void AddPlatformServicesToSkip(HashSet<Type> servicesToSkip)
    { }

    private static partial void ConfigurePlatformLifecycleEvents(ILifecycleBuilder events)
    { }
}
