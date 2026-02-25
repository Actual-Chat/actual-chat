namespace ActualChat.Logging;

/// <summary>
/// A logger that activates <see cref="Sanitizer"/> around each log call,
/// so that <see cref="PrivateString"/> arguments are masked via their <c>ToString()</c>.
/// </summary>
public sealed class SanitizingLogger(ILogger inner) : ILogger
{
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        using var _ = Sanitizer.Activate();
        inner.Log(logLevel, eventId, state, exception, formatter);
    }

    public bool IsEnabled(LogLevel logLevel)
        => inner.IsEnabled(logLevel);

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        => inner.BeginScope(state);
}
