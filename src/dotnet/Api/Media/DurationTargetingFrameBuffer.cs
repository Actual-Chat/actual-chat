namespace ActualChat.Media;

public sealed class DurationTargetingFrameBuffer<TFrame>(
    Func<TFrame, TimeSpan> getOffset,
    Func<TFrame, TimeSpan> getDuration,
    TimeSpan targetDuration = default)
    where TFrame : class
{
    private readonly Lock _lock = new();
    private readonly Queue<TFrame> _frames = new();

    private TaskCompletionSource? _whenChanged;
    private TFrame? _lastFrame;
    private TimeSpan _targetDuration = targetDuration.Positive();
    private bool _isCompleted;

    public int Count {
        get {
            lock (_lock)
                return _frames.Count;
        }
    }

    public bool IsCompleted {
        get {
            lock (_lock)
                return _isCompleted;
        }
    }

    public TimeSpan TargetDuration {
        get {
            lock (_lock)
                return _targetDuration;
        }
    }

    public TimeSpan Duration {
        get {
            lock (_lock)
                return GetDurationUnsafe();
        }
    }

    public void SetTargetDuration(TimeSpan targetDuration)
    {
        TaskCompletionSource? completedWhenChanged;
        lock (_lock) {
            _targetDuration = targetDuration.Positive();
            completedWhenChanged = TakeWhenChangedUnsafe();
        }
        completedWhenChanged?.TrySetResult();
    }

    public void Push(TFrame frame)
    {
        TaskCompletionSource? completedWhenChanged;
        lock (_lock) {
            if (_isCompleted)
                return;

            _frames.Enqueue(frame);
            _lastFrame = frame;
            completedWhenChanged = TakeWhenChangedUnsafe();
        }
        completedWhenChanged?.TrySetResult();
    }

    public void Complete()
    {
        TaskCompletionSource? completedWhenChanged;
        lock (_lock) {
            _isCompleted = true;
            completedWhenChanged = TakeWhenChangedUnsafe();
        }
        completedWhenChanged?.TrySetResult();
    }

    public void Clear()
    {
        TaskCompletionSource? completedWhenChanged;
        lock (_lock) {
            _frames.Clear();
            _lastFrame = null;
            completedWhenChanged = TakeWhenChangedUnsafe();
        }
        completedWhenChanged?.TrySetResult();
    }

    public bool TryRead([NotNullWhen(true)] out TFrame? frame)
    {
        lock (_lock) {
            frame = PeekUnsafe();
            if (frame is null)
                return false;
            if (!CanReleaseUnsafe())
                return false;

            frame = DequeueUnsafe();
            return true;
        }
    }

    public async IAsyncEnumerable<TFrame> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (true) {
            if (TryRead(out var frame)) {
                yield return frame;
                continue;
            }

            Task whenChanged;
            lock (_lock) {
                if (_isCompleted && _frames.Count == 0)
                    yield break;

                whenChanged = (_whenChanged ??= TaskCompletionSourceExt.New()).Task;
            }
            await whenChanged.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    // Private methods

    private bool CanReleaseUnsafe()
        => _targetDuration <= TimeSpan.Zero
            || _isCompleted
            || GetDurationUnsafe() >= _targetDuration;

    private TimeSpan GetDurationUnsafe()
    {
        var first = PeekUnsafe();
        if (first is null || _lastFrame is null)
            return TimeSpan.Zero;

        return (getOffset(_lastFrame) + getDuration(_lastFrame) - getOffset(first)).Positive();
    }

    private TFrame? PeekUnsafe()
        => _frames.TryPeek(out var frame) ? frame : null;

    private TFrame DequeueUnsafe()
    {
        var frame = _frames.Dequeue();
        if (_frames.Count == 0)
            _lastFrame = null;
        return frame;
    }

    private TaskCompletionSource? TakeWhenChangedUnsafe()
    {
        var completedWhenChanged = _whenChanged;
        _whenChanged = null;
        return completedWhenChanged;
    }
}
