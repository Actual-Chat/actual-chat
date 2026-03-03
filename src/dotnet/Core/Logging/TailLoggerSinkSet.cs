namespace ActualChat.Logging;

/// <summary>
/// Manages a collection of <see cref="ILogSink"/> instances and broadcasts log entries.
/// </summary>
public sealed class TailLoggerSinkSet
{
    private readonly ConcurrentDictionary<ILogSink, Unit> _sinks = [];

    public void Add(ILogSink sink)
        => _sinks.TryAdd(sink, default);

    public void Remove(ILogSink sink)
        => _sinks.Remove(sink, out _);

    public void Log(
        string categoryName,
        LogLevel logLevel,
        EventId eventId,
        string message,
        Exception? exception)
    {
        foreach (var sink in _sinks.Keys)
            sink.Log(categoryName, logLevel, eventId, message, exception);
    }

    public override string ToString()
        => $"{GetType().GetName()}({_sinks.Count} sinks)";
}
