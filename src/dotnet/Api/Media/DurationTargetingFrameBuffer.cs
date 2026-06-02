namespace ActualChat.Media;

public sealed class DurationTargetingFrameBuffer<TFrame>(
    Func<TFrame, TimeSpan> getOffset,
    Func<TFrame, TimeSpan> getDuration,
    TimeSpan targetDuration = default)
    where TFrame : class
{
    // Min queued source-duration cushion before speed-up may drop a frame.
    // Dropping the last frame(s) strands the engine's decode/feed loop and
    // starves the platform output → audible clicks. Mirrors the Web pipeline's
    // SPEEDUP_MIN_QUEUE_FRAMES (~3 frames) gate in opus-decoder.ts.
    private static readonly TimeSpan SpeedUpMinQueueDuration = Constants.Audio.FrameDuration * 3;

    private readonly Lock _lock = new();
    private readonly Queue<TFrame> _frames = new();

    private TaskCompletionSource? _whenChanged;
    private TFrame? _lastFrame;
    private TimeSpan _targetDuration = targetDuration.Positive();
    private TimeSpan _skipUntil;
    private TimeSpan _speedUpUntil;
    private int _speedUpDropEveryNFrames;
    private int _speedUpFrameCounter;
    private TimeSpan _droppedDuration;
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

    // Cumulative source-duration removed by skip/speed-up (frames dropped, never
    // played). Engines add this to the played-samples playhead so presentation
    // lag reflects the true source position of the playing sample (catch-up drops
    // advance source time without playing it). Note: drops happen at the read/feed
    // point, ahead of the playhead by the decode+output buffer, so this slightly
    // over-counts during an in-flight catch-up burst (bounded by buffer depth).
    public TimeSpan DroppedDuration {
        get {
            lock (_lock)
                return _droppedDuration;
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
            _skipUntil = TimeSpan.Zero;
            ClearSpeedUpUnsafe();
            completedWhenChanged = TakeWhenChangedUnsafe();
        }
        completedWhenChanged?.TrySetResult();
    }

    public void SkipUntil(TimeSpan sourceOffset)
    {
        TaskCompletionSource? completedWhenChanged;
        lock (_lock) {
            if (sourceOffset > _skipUntil)
                _skipUntil = sourceOffset;
            ClearSpeedUpUnsafe();
            DropSkippedFramesUnsafe();
            completedWhenChanged = TakeWhenChangedUnsafe();
        }
        completedWhenChanged?.TrySetResult();
    }

    public void SpeedUpUntil(TimeSpan sourceOffset, int dropEveryNFrames)
    {
        TaskCompletionSource? completedWhenChanged;
        lock (_lock) {
            if (dropEveryNFrames <= 1 || sourceOffset <= TimeSpan.Zero)
                ClearSpeedUpUnsafe();
            else {
                _speedUpUntil = sourceOffset;
                _speedUpDropEveryNFrames = dropEveryNFrames;
                _speedUpFrameCounter = 0;
            }
            completedWhenChanged = TakeWhenChangedUnsafe();
        }
        completedWhenChanged?.TrySetResult();
    }

    public bool TryRead([NotNullWhen(true)] out TFrame? frame)
    {
        lock (_lock) {
            while (true) {
                DropSkippedFramesUnsafe();
                frame = PeekUnsafe();
                if (frame is null)
                    return false;

                if (getOffset(frame) >= _skipUntil)
                    _skipUntil = TimeSpan.Zero;
                if (!CanReleaseUnsafe())
                    return false;
                if (ShouldDropForSpeedUpUnsafe(frame)) {
                    _droppedDuration += getDuration(frame);
                    DequeueUnsafe();
                    continue;
                }

                frame = DequeueUnsafe();
                return true;
            }
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

    private void DropSkippedFramesUnsafe()
    {
        while (PeekUnsafe() is { } frame && getOffset(frame) < _skipUntil) {
            _droppedDuration += getDuration(frame);
            DequeueUnsafe();
        }
    }

    private bool ShouldDropForSpeedUpUnsafe(TFrame frame)
    {
        if (_speedUpDropEveryNFrames <= 0)
            return false;
        if (getOffset(frame) >= _speedUpUntil) {
            ClearSpeedUpUnsafe();
            return false;
        }
        // Never drop when the queue lacks a cushion: dropping the last frame(s)
        // strands the decode/feed loop and starves the platform output (clicks).
        // Pause speed-up while the buffer is too low; resume once it refills.
        if (GetDurationUnsafe() < SpeedUpMinQueueDuration)
            return false;

        _speedUpFrameCounter++;
        return _speedUpFrameCounter % _speedUpDropEveryNFrames == 0;
    }

    private void ClearSpeedUpUnsafe()
    {
        _speedUpUntil = TimeSpan.Zero;
        _speedUpDropEveryNFrames = 0;
        _speedUpFrameCounter = 0;
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
