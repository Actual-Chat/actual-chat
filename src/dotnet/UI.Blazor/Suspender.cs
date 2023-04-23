namespace ActualChat.UI.Blazor;

public sealed class Suspender
{
    private readonly object _lock = new();
    private volatile TaskCompletionSource<Unit>? _whenUnpausedSource;

    public bool IsSuspended {
        get => _whenUnpausedSource != null;
        set {
            lock (_lock) {
                var oldSource = _whenUnpausedSource;
                var isPaused = oldSource != null;
                if (value == isPaused)
                    return;

                if (value)
                    _whenUnpausedSource = TaskCompletionSourceExt.New<Unit>();
                else {
                    _whenUnpausedSource = null;
                    oldSource?.TrySetResult(default);
                }
            }
        }
    }

    public Task WhenResumed()
        => _whenUnpausedSource?.Task ?? Task.CompletedTask;
}
