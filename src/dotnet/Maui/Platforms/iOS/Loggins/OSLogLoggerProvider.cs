namespace ActualChat.Maui;

public class OSLogLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, OSLogLogger> _loggers = new (StringComparer.Ordinal);

    public ILogger CreateLogger(string name)
        => _loggers.GetOrAdd(name, static x => new OSLogLogger(x));

    public void Dispose()
    { }
}
