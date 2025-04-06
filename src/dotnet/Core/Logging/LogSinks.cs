using ActualLab.Generators;

namespace ActualChat.Logging;

public sealed class LogSinks
{
    private readonly ConcurrentDictionary<ILogSink, Unit> _sinks = [];

    public Symbol Id { get; } = RandomStringGenerator.Default.Next(5);

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
        => $"#{Id}: {_sinks.Count} sinks";
}
