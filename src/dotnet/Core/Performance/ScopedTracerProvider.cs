namespace ActualChat.Performance;

public class ScopedTracerProvider
{
    private static long _lastId;

    public Tracer Tracer { get; }

    public ScopedTracerProvider()
        : this(CreateScopeTracer()) { }
    public ScopedTracerProvider(Tracer tracer)
        => Tracer = tracer;

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
