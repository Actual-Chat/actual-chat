namespace ActualChat.App.Maui.Services.StartupTracing;

internal class DispatcherEveryOperationLogger : IDispatcherOperationLogger
{
    private static readonly TimeSpan _longWorkItemThreshold = TimeSpan.FromMilliseconds(10);
    private Stopwatch _sw = new ();
    private readonly Tracer _tracer;

    public DispatcherEveryOperationLogger()
        => _tracer = Tracer.Default["Dispatcher"];

    public void OnBeforeOperation()
    {
        _sw = Stopwatch.StartNew();
        _tracer.Point("Operation is about to start");
    }

    public void OnAfterOperation()
    {
        _sw.Stop();
        var elapsed = _sw.Elapsed;
        var isLongWorkItem = elapsed >= _longWorkItemThreshold;
        if (isLongWorkItem) {
            var startTime = DateTime.Now - elapsed;
            _tracer.Point(
                $"Long task duration: {TracePoint.FormatDuration(elapsed)}, estimated start time: '{startTime:HH:mm:ss.fff}'");
        }
        else
            _tracer.Point($"Short task duration: {TracePoint.FormatDuration(elapsed)}");
    }
}
