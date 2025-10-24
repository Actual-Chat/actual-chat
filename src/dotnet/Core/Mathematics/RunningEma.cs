namespace ActualChat.Mathematics;

public sealed class RunningEma(float defaultValue, int minSampleCount, double? smoothingFactor = null)
{
    private readonly RunningAverage _average = new (defaultValue);
    private readonly double _smoothingFactor = smoothingFactor ?? 2.0 / (minSampleCount + 1);
    private double? _value = null;

    public int SampleCount { get; private set; }

    public double Value => _value ?? _average.Value;

    public void Reset()
    {
        SampleCount = 0;
        _average.Reset();
        _value = null;
    }

    public void AppendSample(double value)
    {
        SampleCount++;
        var last = _value;
        if (last is null) {
            _average.AppendSample(value);
            if (SampleCount >= minSampleCount)
                _value = _average.Value;
        }
        else
            _value = last.Value + (_smoothingFactor * (value - last.Value));
    }
}
