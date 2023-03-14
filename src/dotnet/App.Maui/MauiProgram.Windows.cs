using ActualChat.Notification.UI.Blazor;
using Microsoft.Maui.LifecycleEvents;
using Serilog;

namespace ActualChat.App.Maui;

public static partial class MauiProgram
{
    private static partial LoggerConfiguration ConfigurePlatformLogger(this LoggerConfiguration loggerConfiguration)
        => loggerConfiguration
            .WriteTo.Debug(
                outputTemplate:"[{Timestamp:HH:mm:ss.fff} {Level:u3} ({ThreadID})] [{SourceContext}] {Message:lj}{NewLine}{Exception}");

    private static partial void AddPlatformServices(this IServiceCollection services)
    {
        services.AddTransient<IDeviceTokenRetriever>(_ => new WindowsDeviceTokenRetriever());
        services.AddScoped<INotificationPermissions>(c => new WindowsNotificationPermissions());
    }

    private static partial void ConfigurePlatformLifecycleEvents(ILifecycleBuilder events)
    { }
}
