using ActualChat.Hosting;
using Microsoft.Extensions.Hosting;
using ActualLab.IO;

namespace ActualChat.UI.Blazor.App;

public static class ClientLogging
{
    public const string OutputTemplate = "{Timestamp:HH:mm:ss.fff} {Level:u3} T{ThreadID} [{SourceContext}] {Message:l}{NewLine}{Exception}";
    public const string DebugOutputTemplate = "{Timestamp:mm:ss.fff} {Level:u3} T{ThreadID} [{SourceContext}] {Message:l}{NewLine}{Exception}";
    public const long FileSizeLimit = 10_000_000L;
    public const string DevLogOutputTemplate = "{ProcessID}: {Timestamp:mm:ss.fff} {Level:u3} T{ThreadID} [{SourceContext}] {Message:l}{NewLine}{Exception}";
    public const long DevLogFileSizeLimit = 100_000_000L;

    public static readonly FilePath DevLog;
    public static LogLevel MinLevel { get; private set; }

    static ClientLogging()
    {
        var devLogEnvVar = Environment.GetEnvironmentVariable("ActualChat_DevLog");
        DevLog = FilePath.New(devLogEnvVar);
    }

    public static ILoggingBuilder ConfigureClientFilters(this ILoggingBuilder logging, AppKind appKind)
    {
        MinLevel = DevLog.IsEmpty ? LogLevel.Information : LogLevel.Debug;
#if DEBUG
        MinLevel = LogLevel.Debug;
#endif
        logging.SetMinimumLevel(MinLevel);
        // We can't use appsettings*.json on the client, so client-side log filters are configured here
        logging
            .AddFilter(null, LogLevel.Information)
            .AddFilter("System", LogLevel.Warning)
            .AddFilter("Microsoft", LogLevel.Warning)
            .AddFilter("ActualChat", MinLevel);
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
        MinLevel = DevLog.IsEmpty && !OrdinalEquals(environment, Environments.Development)
            ? LogLevel.Information
            : LogLevel.Debug;

        logging.SetMinimumLevel(MinLevel);
        // Use appsettings*.json to configure logging filters
        return logging;
    }
}
