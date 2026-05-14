using Sentry.Extensibility;
using SerilogLogger = Serilog.ILogger;

namespace ActualChat.Maui.Sentry;

internal sealed class SerilogDiagnosticLogger(SentryLevel minLevel) : IDiagnosticLogger
{
    private static SerilogLogger GetLog() => Serilog.Log.Logger.ForContext("SourceContext", "Sentry");

    public bool IsEnabled(SentryLevel level) => level >= minLevel;

    public void Log(SentryLevel logLevel, string message, Exception? exception = null, params object?[] args)
    {
        if (!IsEnabled(logLevel))
            return;

        var log = GetLog();
        switch (logLevel) {
        case SentryLevel.Fatal:
        case SentryLevel.Error:
            log.Error(exception, message, args);
            break;
        case SentryLevel.Warning:
            log.Warning(exception, message, args);
            break;
        case SentryLevel.Info:
            log.Information(exception, message, args);
            break;
        default:
            log.Debug(exception, message, args);
            break;
        }
    }
}
