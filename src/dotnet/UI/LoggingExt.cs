using ActualLab.IO;
using Microsoft.Extensions.Hosting;

namespace ActualChat.UI;

public static class LoggingExt
{
    public const string OutputTemplate = "{Timestamp:HH:mm:ss.fff} {Level:u3} T{ThreadID} [{SourceContext}] {Message:l}{NewLine}{Exception}";
    public const long FileSizeLimit = 10_000_000L;
    public const int RetainedFileCountLimit = 3;
    public const long DevLogFileSizeLimit = 100_000_000L;

    public static readonly FilePath DevLog;
    public static LogLevel MinLevel { get; private set; }

    private static readonly string[] NotificationLogCategories = [
        "ActualChat.App.Maui.FirebaseMessagingService",
        "ActualChat.App.Maui.NotificationHelper",
        "ActualChat.App.Maui.AndroidDeviceNotifications",
        "ActualChat.App.Maui.AppDelegate",
        "ActualChat.App.Maui.IosPushNotifications",
        "ActualChat.App.Maui.IosDeviceNotifications",
        "ActualChat.App.Maui.Services.MauiNotifications",
        "ActualChat.UI.Blazor.App.NotificationUI",
        "ActualChat.UI.Blazor.App.Services.NotificationReconciler",
        "ActualChat.UI.Blazor.App.Services.WebDeviceNotifications",
    ];

    static LoggingExt()
    {
        var devLogEnvVar = Environment.GetEnvironmentVariable("ActualChat_DevLog");
        DevLog = FilePath.New(devLogEnvVar);
#if DEBUG
        MinLevel = LogLevel.Debug;
#else
        MinLevel = DevLog.IsEmpty
            ? LogLevel.Information
            : LogLevel.Debug;
#endif
    }

    public static ILoggingBuilder ConfigureClientFilters(this ILoggingBuilder logging, AppKind appKind)
    {
        logging.SetMinimumLevel(MinLevel);
        // We can't use appsettings*.json on the client, so client-side log filters are configured here
        logging
            .AddFilter(null, LogLevel.Information)
            .AddFilter("System", LogLevel.Warning)
            .AddFilter("Microsoft", LogLevel.Warning)
            .AddFilter("ActualChat", MinLevel);
        // Notifications troubleshooting: keep the push receive/show/register/reconcile path at Debug
        // even in Release builds, where MinLevel is Information and would drop these LogDebug lines.
        foreach (var category in NotificationLogCategories)
            logging.AddFilter(category, LogLevel.Debug);
#if false
        // Extra logging for profiling / perf. works:
        logging
           .AddFilter("Microsoft.AspNetCore.Components.WebView", LogLevel.Debug) // WebView
           .AddFilter("Microsoft.AspNetCore.Components.RenderTree.Renderer", LogLevel.Debug); // Blazor renderer
#endif
        return logging;
    }

    public static ILoggingBuilder ConfigureServerFilters(this ILoggingBuilder logging, string environment)
    {
        MinLevel = DevLog.IsEmpty && environment != Environments.Development
            ? LogLevel.Information
            : LogLevel.Debug;

        logging.SetMinimumLevel(MinLevel);
        // Use appsettings*.json to configure logging filters
        return logging;
    }
}
