namespace ActualChat.Logging;

/// <summary>
/// Receives log entries for custom processing or forwarding.
/// </summary>
public interface ILogSink
{
    void Log(string categoryName, LogLevel logLevel, EventId eventId, string message, Exception? exception);
}
