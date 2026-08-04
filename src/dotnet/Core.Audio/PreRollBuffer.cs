namespace ActualChat.Audio;

/// <summary>
/// A bounded, one-shot capture buffer that fills from a native tap before the app's recorder
/// exists and is drained once, keeping the most recent <see cref="Capacity"/> samples. The token
/// ties its content to the capture that armed it, so a buffer abandoned by a failed capture can
/// never be drained by an unrelated later recording.
/// </summary>
public sealed class PreRollBuffer
{
    private readonly Lock _lock = new();
    private readonly float[] _samples;
    private int _head;
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

        // Runs on the real-time audio thread once per tap callback, so it only ever moves the
        // incoming samples: advancing _head is what drops the oldest ones.
        lock (_lock) {
            if (_isDrained)
                return false;

            var capacity = _samples.Length;
            // A slow boot must degrade to "lost the first words", not to "lost everything": the
            // oldest samples are dropped so the newest Capacity worth always survive.
            if (samples.Length >= capacity) {
                samples[^capacity..].CopyTo(_samples);
                (_head, _count, _isOverflowed) = (0, capacity, true);
                return true;
            }

            var dropCount = _count + samples.Length - capacity;
            if (dropCount > 0) {
                _head = (_head + dropCount) % capacity;
                _count -= dropCount;
                _isOverflowed = true;
            }

            var tail = (_head + _count) % capacity;
            var headLength = Math.Min(samples.Length, capacity - tail);
            samples[..headLength].CopyTo(_samples.AsSpan(tail));
            if (headLength < samples.Length)
                samples[headLength..].CopyTo(_samples);
            _count += samples.Length;
            return true;
        }
    }

    public float[]? TryDrain(long token, int minSampleCount)
    {
        lock (_lock) {
            if (_isDrained || token != Token || _count < minSampleCount)
                return null;

            _isDrained = true;
            var result = new float[_count];
            var headLength = Math.Min(_count, _samples.Length - _head);
            _samples.AsSpan(_head, headLength).CopyTo(result);
            if (headLength < _count)
                _samples.AsSpan(0, _count - headLength).CopyTo(result.AsSpan(headLength));
            return result;
        }
    }
}
