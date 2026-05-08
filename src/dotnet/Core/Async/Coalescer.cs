namespace ActualChat;

/// <summary>
/// Runs <paramref name="action"/> for the latest submitted value, with at most
/// one invocation in flight. Intermediate values submitted while a previous
/// invocation is in progress are dropped (last-write-wins).
/// </summary>
/// <remarks>
/// Use for continuous signals where only the latest value matters
/// (audio power, scroll/drag position, progress). For discrete transitions
/// (open/close, start/stop) prefer a queue or direct invocation —
/// coalescing can desync receiver state by collapsing transitions.
///
/// Exceptions thrown by <paramref name="action"/> are swallowed: a single
/// failed invocation must not stop the pump or leak through the
/// fire-and-forget submit path.
/// </remarks>
public sealed class Coalescer<T>(Func<T, ValueTask> action)
{
    private readonly Lock _lock = new();
    private T _pendingValue = default!;
    private bool _hasPending;
    private bool _isFlushing;

    public void Submit(T value)
    {
        bool startFlush;
        lock (_lock) {
            _pendingValue = value;
            _hasPending = true;
            startFlush = !_isFlushing;
            if (startFlush)
                _isFlushing = true;
        }
        if (startFlush)
            _ = Flush();
    }

    private async Task Flush()
    {
        while (true) {
            T value;
            lock (_lock) {
                if (!_hasPending) {
                    _isFlushing = false;
                    return;
                }
                value = _pendingValue;
                _pendingValue = default!;
                _hasPending = false;
            }
            try {
                await action(value).ConfigureAwait(false);
            }
            catch {
                // Best-effort: a failed action must not block subsequent submissions.
            }
        }
    }
}

public static class Coalescer
{
    public static Coalescer<T> New<T>(Func<T, ValueTask> action) => new(action);
}
