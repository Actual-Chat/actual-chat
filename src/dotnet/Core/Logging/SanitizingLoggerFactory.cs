namespace ActualChat.Logging;

/// <summary>
/// A logger factory that wraps every logger it creates with <see cref="SanitizingLogger"/>,
/// which activates <see cref="Sanitizer"/> around each <see cref="ILogger.Log{TState}"/> call.
/// This is more efficient than wrapping each <see cref="ILoggerProvider"/> individually.
/// </summary>
public sealed class SanitizingLoggerFactory(ILoggerFactory innerFactory, bool mustSanitize = true)
    : ILoggerFactory
{
    public ILogger CreateLogger(string categoryName)
    {
        var logger = innerFactory.CreateLogger(categoryName);
        return mustSanitize
            ? new SanitizingLogger(logger)
            : logger;
    }

    public void AddProvider(ILoggerProvider provider)
        => innerFactory.AddProvider(provider);

    public void Dispose()
        => innerFactory.Dispose();
}
