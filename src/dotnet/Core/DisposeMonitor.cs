namespace ActualChat;

public class DisposeMonitor : IDisposable
{
    private readonly object _lock = new();
    private readonly CancellationTokenSource _disposeTokenSource = new();
    private readonly TaskCompletionSource<Unit> _whenDisposedSource = TaskCompletionSourceExt.New<Unit>();

    public bool IsDisposed { get; set; }
    public Task WhenDisposed => _whenDisposedSource.Task;
    public CancellationToken DisposeToken { get; }
    public event Action? Disposed;

    public DisposeMonitor()
        => DisposeToken = _disposeTokenSource.Token;

    public void Dispose()
    {
        if (IsDisposed) return;
        lock (_lock) {
            if (IsDisposed) return;
            IsDisposed = true;
            _whenDisposedSource.TrySetResult(default);
            _disposeTokenSource.CancelAndDisposeSilently();
            Disposed?.Invoke();
        }
    }
}
