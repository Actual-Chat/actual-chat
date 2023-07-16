using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Notification.UI.Blazor;
using ActualChat.UI.Blazor.App.Services;
using Microsoft.Maui.LifecycleEvents;
using Serilog;

namespace ActualChat.App.Maui;

public static partial class MauiProgram
{
    public static partial LoggerConfiguration ConfigurePlatformLogger(LoggerConfiguration loggerConfiguration)
    {
        loggerConfiguration = loggerConfiguration
            .WriteTo.Debug(
                outputTemplate:"[{Timestamp:HH:mm:ss.fff} {Level:u3} ({ThreadID})] [{SourceContext}] {Message:lj}{NewLine}{Exception}");

        var localFolder = Windows.Storage.ApplicationData.Current.LocalFolder;
        var timeSuffix = DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var fileName = $"actual.chat.{timeSuffix}.log";
        loggerConfiguration = loggerConfiguration.WriteTo.File(
            Path.Combine(localFolder.Path, "Logs", fileName),
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}",
            fileSizeLimitBytes: 20 * 1024 * 1024);

        return loggerConfiguration;
    }

    public static partial string? GetAppSettingsFilePath()
        => null;

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
