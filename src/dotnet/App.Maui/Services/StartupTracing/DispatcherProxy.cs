namespace ActualChat.App.Maui.Services.StartupTracing;

internal class DispatcherProxy(IDispatcher original, bool logAllOperations) : IDispatcher
{
    private readonly IDispatcherOperationLogger _operationLogger = CreateOperationLogger(logAllOperations);

    public bool IsDispatchRequired => original.IsDispatchRequired;

    private static IDispatcherOperationLogger CreateOperationLogger(bool logEverything)
        => logEverything
            ? new DispatcherEveryOperationLogger()
            : new DispatcherLongOperationLogger();

    public bool Dispatch(Action action)
        => original.Dispatch(WrapAction(action));

    public bool DispatchDelayed(TimeSpan delay, Action action)
        => original.DispatchDelayed(delay, WrapAction(action));

    public IDispatcherTimer CreateTimer()
        => original.CreateTimer();

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
