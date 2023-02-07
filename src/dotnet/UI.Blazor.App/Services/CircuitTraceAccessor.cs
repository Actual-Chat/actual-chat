namespace ActualChat.UI.Blazor.App.Services;

public class CircuitTraceAccessor : ITraceAccessor, IDisposable
{
    public ITraceSession? Trace { get; private set; }
    public static Func<ITraceSession>? Factory { get; set; }
    public void Init()
        => Trace = Factory?.Invoke();

    public void Dispose()
        => Trace?.Track("CircuitTraceAccessor.Dispose");
}
