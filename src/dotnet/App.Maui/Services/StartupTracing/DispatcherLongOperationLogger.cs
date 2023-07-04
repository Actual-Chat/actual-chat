namespace ActualChat.App.Maui.Services.StartupTracing;

internal class DispatcherLongOperationLogger : IDispatcherOperationLogger
{
    private const int ShortTasksBatchSize = 30;
    private static readonly TimeSpan _longWorkItemThreshold = TimeSpan.FromMilliseconds(10);
    private CpuTimestamp _operationStartedAt = CpuTimestamp.Now;
    private long _shortWorkItemNumber;
    private TimeSpan _showWorkItemTotalDuration;
    private readonly Tracer _tracer = Tracer.Default[nameof(DispatcherProxy)];

    public void OnBeforeOperation()
        => _operationStartedAt = CpuTimestamp.Now;

    public void OnAfterOperation()
    {
        var elapsed = _operationStartedAt.Elapsed;
        var isLongOperation = elapsed >= _longWorkItemThreshold;
        if (!isLongOperation) {
            _shortWorkItemNumber++;
            _showWorkItemTotalDuration += elapsed;
        }
        if (_shortWorkItemNumber >= ShortTasksBatchSize || isLongOperation) {
            _tracer.Point(
                $"Short tasks duration: {TracePoint.FormatDuration(_showWorkItemTotalDuration)} ({_shortWorkItemNumber} tasks)");
            _shortWorkItemNumber = 0;
            _showWorkItemTotalDuration = TimeSpan.Zero;
        }
        if (isLongOperation) {
            var startTime = DateTime.Now - elapsed;
            _tracer.Point(
                $"Long task duration: {TracePoint.FormatDuration(elapsed)}, estimated start time: '{startTime:HH:mm:ss.fff}'");
        }
    }
}
