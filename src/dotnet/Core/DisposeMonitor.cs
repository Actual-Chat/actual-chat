namespace ActualChat;

public class DisposeMonitor : IDisposable
{
    private readonly object _lock = new();
    private readonly CancellationTokenSource _disposedCts = new();

    public bool IsDisposed { get; set; }
    public Task<Unit> WhenDisposed { get; } = TaskSource.New<Unit>(true).Task;
    public CancellationToken DisposeToken { get; }
    public event Action? Disposed;

    public DisposeMonitor()
        => DisposeToken = _disposedCts.Token;

    public void Dispose()
    {
        if (IsDisposed) return;
        lock (_lock) {
            if (IsDisposed) return;
            IsDisposed = true;
            TaskSource.For(WhenDisposed).TrySetResult(default);
            _disposedCts.CancelAndDisposeSilently();
            Disposed?.Invoke();
        }
    }
}
