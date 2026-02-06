namespace ActualChat.Logging;

/// <summary>
/// A logger that forwards log entries to <see cref="LogSinks"/>.
/// </summary>
public class TailLogger(IServiceProvider services, string categoryName) : ILogger
{
    private LogSinks Sinks => field ??= services.GetRequiredService<LogSinks>();

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        Sinks.Log(categoryName, logLevel, eventId, message, exception);
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public IDisposable BeginScope<TState>(TState state) where TState : notnull
        => null!;
}
