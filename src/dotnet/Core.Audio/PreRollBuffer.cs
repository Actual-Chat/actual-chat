namespace ActualChat.Audio;

/// <summary>
/// A bounded, one-shot capture buffer that fills from a native tap before the app's recorder
/// exists and is drained once. The token ties its content to the capture that armed it, so a
/// buffer abandoned by a failed capture can never be drained by an unrelated later recording.
/// </summary>
public sealed class PreRollBuffer
{
    private readonly Lock _lock = new();
    private readonly float[] _samples;
    private int _count;
    private bool _isOverflowed;
    private bool _isDrained;

    public long Token { get; }
    public int SampleRate { get; }
    public int Capacity => _samples.Length;

    public int Count {
        get {
            lock (_lock)
                return _count;
        }
    }

    public bool IsOverflowed {
        get {
            lock (_lock)
                return _isOverflowed;
        }
    }

    public TimeSpan Duration => TimeSpan.FromSeconds((double)Count / SampleRate);

    public PreRollBuffer(long token, int sampleRate, int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        Token = token;
        SampleRate = sampleRate;
        _samples = new float[capacity];
    }

    public bool TryAppend(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty)
            return true;

        lock (_lock) {
            if (_isDrained || _isOverflowed)
                return false;

            if (samples.Length > _samples.Length - _count) {
                // The boot budget was blown. A fragment whose start is missing would be sent as
                // if it were the whole reply, so the buffer is voided rather than truncated.
                _isOverflowed = true;
                _count = 0;
                return false;
            }

            samples.CopyTo(_samples.AsSpan(_count));
            _count += samples.Length;
            return true;
        }
    }

    public float[]? TryDrain(long token, int minSampleCount)
    {
        lock (_lock) {
            if (_isDrained || _isOverflowed || token != Token || _count < minSampleCount)
                return null;

            _isDrained = true;
            return _samples.AsSpan(0, _count).ToArray();
        }
    }
}
