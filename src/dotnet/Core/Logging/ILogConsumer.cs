namespace ActualChat.Logging;

public interface ILogSink
{
    void Log(string categoryName, LogLevel logLevel, EventId eventId, string message, Exception? exception);
}
