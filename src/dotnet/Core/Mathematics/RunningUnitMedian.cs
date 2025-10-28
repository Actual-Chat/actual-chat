namespace ActualChat.Mathematics;

public sealed class RunningUnitMedian(int bucketCount = 100, double defaultValue = 0.5)
{
    private readonly int[] _buckets = new int[bucketCount];
    private readonly double _halfBucketSize = 0.5 / bucketCount;
    private double? _value = null;

    public int SampleCount { get; private set; } = 0;

    public double Value {
        get {
            if (_value is not null) return _value.Value;

            if (SampleCount <= 0) {
                _value = defaultValue;
                return _value.Value;
            }
            var halfSampleCount = SampleCount / 2.0;
            var runningCount = 0;
            for (var i = 0; i < _buckets.Length; i++) {
                runningCount += _buckets[i];
                if (runningCount >= halfSampleCount) {
                    _value = ((2 * i) + 1) * _halfBucketSize;
                    return _value.Value;
                }
            }
            _value = ((2 * _buckets.Length) - 1) * _halfBucketSize;
            return _value.Value;
        }
    }

    public void Reset()
    {
        Array.Fill(_buckets, 0);
        SampleCount = 0;
        _value = null;
    }

    public void AppendSample(double value)
    {
        value = value.Clamp(0, 1);
        var idx = (int)Math.Floor(value * _buckets.Length);
        if (idx < 0)
            idx = 0;
        else if (idx >= _buckets.Length)
            idx = _buckets.Length - 1;
        _buckets[idx]++;
        SampleCount++;
        _value = null;
    }
}
