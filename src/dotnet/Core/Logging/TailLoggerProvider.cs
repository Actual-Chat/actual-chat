namespace ActualChat.Logging;

/// <summary>
/// Creates <see cref="TailLogger"/> instances for forwarding logs to <see cref="LogSinks"/>.
/// </summary>
public class TailLoggerProvider(IServiceProvider services) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName)
        => new TailLogger(services, categoryName);

    public void Dispose()
    { }
}
