namespace ActualChat.UI.Blazor.App.Services;

public class CircuitTracerProvider : TracerProvider, IDisposable
{
    private static long _lastId;

    public CircuitTracerProvider()
    {
        var defaultTracer = Tracer.Default;
        if (!defaultTracer.IsEnabled) {
            Tracer = Tracer.None;
            return;
        }

        var processId = RuntimeInfo.Process.MachinePrefixedId.Value;
        var id = Interlocked.Increment(ref _lastId);
        Tracer = new Tracer($"{defaultTracer.Name}.Circuit-{processId}-{id.Format()}", defaultTracer.Writer);
    }

    public void Dispose()
        => Tracer.Point("Dispose");
}
