using System.Text;
using CoreFoundation;

namespace ActualChat.Maui;

public class OSLogLogger(string name) : ILogger<object>
{
    private const string LoglevelPadding = ": ";
    private static readonly string NewLineWithMessagePadding;
    private readonly StringBuilder _logBuilder = new ();

    private readonly CoreFoundation.OSLog _osLog = new (NSBundle.MainBundle.BundleIdentifier, "dev.voxt.ai"); // TODO: different for dev and prod
    private readonly string _name = name ?? throw new ArgumentNullException(nameof(name));

    static OSLogLogger()
    {
        var logLevelString = GetLogLevelString(LogLevel.Information);
        var messagePadding = new string(' ', logLevelString.Length + LoglevelPadding.Length);
        NewLineWithMessagePadding = Environment.NewLine + messagePadding;
    }

    public OSLogLogger()
        : this(string.Empty)
    { }

    public IDisposable BeginScope<TState>(TState state) where TState : notnull
        => NoOpDisposable.Instance;

    public bool IsEnabled(LogLevel logLevel)
        => logLevel != LogLevel.None;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        if (formatter == null)
            throw new ArgumentNullException(nameof(formatter));

        var message = formatter(state, exception);

        if (!string.IsNullOrEmpty(message) || exception != null) {
            WriteMessage(logLevel,
                _name,
                eventId.Id,
                message,
                exception);
        }
    }

    private void WriteMessage(
        LogLevel logLevel,
        string logName,
        int eventId,
        string message,
        Exception? exception)
    {
        lock (_logBuilder) {
            try {
                CreateDefaultLogMessage(_logBuilder,
                    logLevel,
                    logName,
                    eventId,
                    message,
                    exception);
                var formattedMessage = _logBuilder.ToString();

                var osLogLevel = logLevel switch {
                    LogLevel.Critical or LogLevel.Error => OSLogLevel.Fault,
                    LogLevel.Warning => OSLogLevel.Error,
                    LogLevel.Information => OSLogLevel.Default,
                    LogLevel.Debug or LogLevel.Trace => OSLogLevel.Debug,
                    _ => OSLogLevel.Default,
                };

                _osLog.Log(osLogLevel, formattedMessage);
            }
            catch (Exception ex) {
                Console.Error.WriteLine($"Failed to log \"{message}\": {ex}");
            }
            finally {
                _logBuilder.Clear();
            }
        }
    }

    private void CreateDefaultLogMessage(
        StringBuilder logBuilder,
        LogLevel logLevel,
        string logName,
        int eventId,
        string message,
        Exception? exception)
    {
        logBuilder.Append(GetLogLevelString(logLevel));
        logBuilder.Append(LoglevelPadding);
        logBuilder.Append(logName);
        logBuilder.Append("[");
        logBuilder.Append(eventId);
        logBuilder.Append("] ");

        if (!string.IsNullOrEmpty(message)) {
            var len = logBuilder.Length;
            logBuilder.Append(message);
            logBuilder.Replace(Environment.NewLine, NewLineWithMessagePadding, len, message.Length);
        }

        if (exception != null) {
            // exception message
            logBuilder.AppendLine();
            logBuilder.Append(exception);
        }
    }

    private static string GetLogLevelString(LogLevel logLevel)
        => logLevel switch {
            LogLevel.Trace => "trce",
            LogLevel.Debug => "dbug",
            LogLevel.Information => "info",
            LogLevel.Warning => "warn",
            LogLevel.Error => "fail",
            LogLevel.Critical => "crit",
            _ => throw new ArgumentOutOfRangeException(nameof(logLevel)),
        };

    private class NoOpDisposable : IDisposable
    {
        public static readonly NoOpDisposable Instance = new ();

        public void Dispose() { }
    }
}
