namespace ActualChat.App.Maui.Services.StartupTracing;

internal class DispatcherProxy : IDispatcher
{
    private readonly IDispatcher _original;
    private readonly IDispatcherOperationsLogger _operationsLogger;

    public DispatcherProxy(IDispatcher original, bool logAllOperations)
    {
        _original = original;
        _operationsLogger = CreateDispatcherLogger(logAllOperations);
    }

    private IDispatcherOperationsLogger CreateDispatcherLogger(bool logAllOperations)
        => logAllOperations ? new AllDispatcherOperationsLogger() : new LongDispatcherOperationsLogger();

    public bool Dispatch(Action action)
        => _original.Dispatch(WrapAction(action));

    public bool DispatchDelayed(TimeSpan delay, Action action)
        => _original.DispatchDelayed(delay, WrapAction(action));

    public IDispatcherTimer CreateTimer()
        => _original.CreateTimer();

    private Action WrapAction(Action action)
        => () => {
            _operationsLogger.OnBeforeOperation();
            try {
                action();
            }
            finally {
                _operationsLogger.OnAfterOperation();
            }
        };

    public bool IsDispatchRequired => _original.IsDispatchRequired;
}

internal interface IDispatcherOperationsLogger
{
    void OnBeforeOperation();
    void OnAfterOperation();
}

internal class LongDispatcherOperationsLogger : IDispatcherOperationsLogger
{
    private const int ShortTasksBatchSize = 30;
    private static readonly TimeSpan _longWorkItemThreshold = TimeSpan.FromMilliseconds(10);
    private Stopwatch _sw = new ();
    private long _shortWorkItemNumber;
    private TimeSpan _showWorkItemTotalDuration;
    private readonly ITraceSession _trace;

    public LongDispatcherOperationsLogger()
        => _trace = TraceSession.Default;

    public void OnBeforeOperation()
        => _sw = Stopwatch.StartNew();

    public void OnAfterOperation()
    {
        _sw.Stop();
        var elapsed = _sw.Elapsed;
        var isLongWorkItem = elapsed >= _longWorkItemThreshold;
        if (!isLongWorkItem) {
            _shortWorkItemNumber++;
            _showWorkItemTotalDuration += elapsed;
        }
        if (_shortWorkItemNumber >= ShortTasksBatchSize || isLongWorkItem) {
            _trace.Track(
                $"Dispatcher : Short tasks total duration is {_showWorkItemTotalDuration.TotalMilliseconds:N1}ms ({_shortWorkItemNumber} tasks)");
            _shortWorkItemNumber = 0;
            _showWorkItemTotalDuration = TimeSpan.Zero;
        }
        if (isLongWorkItem) {
            var startTime = DateTime.Now - elapsed;
            _trace.Track(
                $"Dispatcher : Long task duration is {elapsed.TotalMilliseconds:N1}ms. Estimated start time is '{startTime:HH:mm:ss.fff}'");
        }
    }
}

internal class AllDispatcherOperationsLogger : IDispatcherOperationsLogger
{
    private static readonly TimeSpan _longWorkItemThreshold = TimeSpan.FromMilliseconds(10);
    private Stopwatch _sw = new ();
    private readonly ITraceSession _trace;

    public AllDispatcherOperationsLogger()
        => _trace = TraceSession.Default;

    public void OnBeforeOperation()
    {
        _sw = Stopwatch.StartNew();
        _trace.Track($"Dispatcher : operation is about to start");
    }

    public void OnAfterOperation()
    {
        _sw.Stop();
        var elapsed = _sw.Elapsed;
        var isLongWorkItem = elapsed >= _longWorkItemThreshold;
        if (isLongWorkItem) {
            var startTime = DateTime.Now - elapsed;
            _trace.Track(
                $"Dispatcher : Long task duration is {elapsed.TotalMilliseconds:N1}ms. Estimated start time is '{startTime:HH:mm:ss.fff}'");
        }
        else {
            _trace.Track($"Dispatcher : Short task duration is {elapsed.TotalMilliseconds:N1}ms");
        }
    }
}
