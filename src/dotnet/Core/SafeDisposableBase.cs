namespace ActualChat;

public abstract class SafeDisposableBase : IDisposable, IHasDisposeStatus
{
    private volatile int _isDisposed;

    public bool IsDisposed => _isDisposed != 0;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
            Dispose(true);
    }

    // Protected methods

    protected abstract void Dispose(bool disposing);

    protected void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(IsDisposed, this);
}
