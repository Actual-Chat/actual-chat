namespace ActualChat.Logging;

/// <summary>
/// A logger that forwards log entries to <see cref="TailLoggerSinkSet"/>.
/// </summary>
public class TailLogger(IServiceProvider services, string categoryName) : ILogger
{
    private TailLoggerSinkSet? _sinks;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        try {
            _sinks ??= services.GetRequiredService<TailLoggerSinkSet>();
            var message = formatter(state, exception);
            _sinks.Log(categoryName, logLevel, eventId, message, exception);
        }
        catch (ObjectDisposedException) {
            // Ignore: DI container is disposed during shutdown
        }
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public IDisposable BeginScope<TState>(TState state) where TState : notnull
        => null!;
}
