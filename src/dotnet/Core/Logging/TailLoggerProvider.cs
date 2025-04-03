namespace ActualChat.Logging;

public class TailLoggerProvider(IServiceProvider services) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName)
        => new TailLogger(services, categoryName);

    public void Dispose()
    { }
}
