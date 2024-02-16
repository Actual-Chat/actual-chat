namespace ActualChat.App.Maui.Services.StartupTracing;

internal class DispatcherEveryOperationLogger : IDispatcherOperationLogger
{
    private static readonly TimeSpan _longWorkItemThreshold = TimeSpan.FromMilliseconds(10);
    private CpuTimestamp _lastOperationStartedAt;
    private readonly Tracer _tracer = Tracer.Default[nameof(DispatcherProxy)];

    public void OnBeforeOperation()
    {
        _lastOperationStartedAt = CpuTimestamp.Now;
        // ReSharper disable once ExplicitCallerInfoArgument
        _tracer.Point("Operation is about to start");
    }

    public void OnAfterOperation()
    {
        var elapsed = CpuTimestamp.Now - _lastOperationStartedAt;
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
