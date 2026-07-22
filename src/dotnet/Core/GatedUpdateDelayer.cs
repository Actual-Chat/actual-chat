namespace ActualChat;

/// <summary>
/// An <see cref="IUpdateDelayer"/> that holds updates while its gate is closed:
/// a pending delay resumes once <see cref="Open"/> is called, so the dependent
/// <see cref="IComputedState"/> stays stale (but retained) instead of recomputing.
/// </summary>
public sealed class GatedUpdateDelayer(IUpdateDelayer inner, bool isOpen = false) : IUpdateDelayer
{
    private volatile TaskCompletionSource<Unit> _gate = isOpen
        ? TaskCompletionSourceExt.New<Unit>().WithResult(default)
        : TaskCompletionSourceExt.New<Unit>();

    public bool IsOpen => _gate.Task.IsCompleted;

    public async Task Delay(int retryCount, CancellationToken cancellationToken = default)
    {
        await _gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        await inner.Delay(retryCount, cancellationToken).ConfigureAwait(false);
    }

    public void Open()
        => _gate.TrySetResult(default);

    public void Close()
    {
        if (_gate.Task.IsCompleted)
            _gate = TaskCompletionSourceExt.New<Unit>();
    }
}
