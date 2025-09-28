namespace ActualChat;

public interface IRunnable
{
    Task Run(IRunnableRunner runner, CancellationToken cancellationToken);
}

public static class Runnable
{
    public static IRunnable New(Func<IRunnableRunner, CancellationToken, Task> func)
        => new FuncRunnable(func);

    // Nested types

    private sealed class FuncRunnable(Func<IRunnableRunner, CancellationToken, Task> func) : IRunnable
    {
        public Task Run(IRunnableRunner runner, CancellationToken cancellationToken)
            => func.Invoke(runner, cancellationToken);
    }
}

public sealed class StartedRunnable : IDisposable
{
    private CancellationTokenSource? _stopTokenSource;
    private Action<StartedRunnable>? _onDispose;

    public IRunnableRunner RunnableRunner { get; }
    public IRunnable Runnable { get; }
    public CancellationToken StopToken { get; }
    public Task Task { get; }

    public StartedRunnable(
        IRunnableRunner runner,
        IRunnable runnable,
        CancellationToken cancellationToken,
        Action<StartedRunnable> onDispose)
    {
        RunnableRunner = runner;
        Runnable = runnable;
        _stopTokenSource = cancellationToken.CreateLinkedTokenSource();
        StopToken = _stopTokenSource.Token;
        _onDispose = onDispose;
        Task = BackgroundTask.Run(() => runnable.Run(runner, StopToken), StopToken);
    }

    public void Dispose()
    {
        if (_onDispose is not { } onDispose)
            return;

        _onDispose = null;
        Stop();
        onDispose.Invoke(this);
    }

    public void Stop()
    {
        if (_stopTokenSource is not { } stopTokenSource)
            return;

        _stopTokenSource = null;
        stopTokenSource.CancelAndDisposeSilently();
    }
}
