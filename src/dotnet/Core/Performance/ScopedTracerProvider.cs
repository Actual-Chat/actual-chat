namespace ActualChat.Performance;

public class ScopedTracerProvider(Tracer tracer)
{
    private static long _lastId;

    public Tracer Tracer { get; } = tracer;

    public ScopedTracerProvider()
        : this(CreateScopeTracer()) { }

    public void Dispose()
        => Tracer.Point(nameof(Dispose));

    // Private methods

    private static Tracer CreateScopeTracer()
    {
        var defaultTracer = Tracer.Default;
        if (!defaultTracer.IsEnabled)
            return Tracer.None;

        var processId = RuntimeInfo.Process.MachinePrefixedId.Value;
        var id = Interlocked.Increment(ref _lastId);
        return new Tracer($"{defaultTracer.Name}.Scope-{processId}-{id.Format()}", defaultTracer.Writer);
    }
}
