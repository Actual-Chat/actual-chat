using ActualChat.Hosting;
using Microsoft.Extensions.Hosting;
using ActualLab.IO;

namespace ActualChat.UI.Blazor.App;

public static class AppLogging
{
    public const string OutputTemplate = "{Timestamp:HH:mm:ss.fff} {Level:u3} T{ThreadID} [{SourceContext}] {Message:l}{NewLine}{Exception}";
    public const string DebugOutputTemplate = "{Timestamp:mm:ss.fff} {Level:u3} T{ThreadID} [{SourceContext}] {Message:l}{NewLine}{Exception}";
    public const long FileSizeLimit = 10_000_000L;

    public static readonly bool IsDevLogRequested;
    public static readonly FilePath DevLogPath;
    public static LogLevel MinLevel { get; private set; }

    static AppLogging()
    {
        var devLogEnvVar = Environment.GetEnvironmentVariable("ActualChat_DevLog");
        IsDevLogRequested = !devLogEnvVar.IsNullOrEmpty();
        DevLogPath = FilePath.New(devLogEnvVar);
    }

    public static ILoggingBuilder ConfigureClientFilters(this ILoggingBuilder logging, ClientKind clientKind)
    {
        MinLevel = IsDevLogRequested ? LogLevel.Debug : LogLevel.Information;
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
        return logging;
    }

    public static ILoggingBuilder ConfigureServerFilters(this ILoggingBuilder logging, string environment)
    {
        MinLevel = LogLevel.Information;
        if (IsDevLogRequested || OrdinalEquals(environment, Environments.Development))
            MinLevel = LogLevel.Debug;

        logging.SetMinimumLevel(MinLevel);
        // Use appsettings*.json to configure logging filters
        return logging;
    }
}
