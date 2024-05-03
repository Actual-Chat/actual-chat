namespace ActualChat.Performance;

public sealed class ScopedTracerProvider
{
    private static long _lastId;

    public Tracer Tracer { get; }

    public ScopedTracerProvider(Tracer rootTracer)
    {
        if (!rootTracer.IsEnabled) {
            Tracer = Tracer.None;
            return;
        }

        var processId = RuntimeInfo.Process.MachinePrefixedId.Value;
        var id = Interlocked.Increment(ref _lastId);
        Tracer = new Tracer($"{rootTracer.Name}.Scope-{processId}-{id.Format()}", rootTracer.Writer);
    }

    public void Dispose()
        => Tracer.Point();
}
