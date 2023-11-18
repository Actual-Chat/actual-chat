namespace ActualChat.App.Maui.Services.StartupTracing;

internal class DispatcherProxy : IDispatcher
{
    private readonly IDispatcher _original;
    private readonly IDispatcherOperationLogger _operationLogger;

    public bool IsDispatchRequired => _original.IsDispatchRequired;

    public DispatcherProxy(IDispatcher original, bool logAllOperations)
    {
        _original = original;
        _operationLogger = CreateOperationLogger(logAllOperations);
    }

    private static IDispatcherOperationLogger CreateOperationLogger(bool logEverything)
        => logEverything
            ? new DispatcherEveryOperationLogger()
            : new DispatcherLongOperationLogger();

    public bool Dispatch(Action action)
        => _original.Dispatch(WrapAction(action));

    public bool DispatchDelayed(TimeSpan delay, Action action)
        => _original.DispatchDelayed(delay, WrapAction(action));

    public IDispatcherTimer CreateTimer()
        => _original.CreateTimer();

    private Action WrapAction(Action action)
        => () => {
            _operationLogger.OnBeforeOperation();
            try {
                action();
            }
            finally {
                _operationLogger.OnAfterOperation();
            }
        };
}
