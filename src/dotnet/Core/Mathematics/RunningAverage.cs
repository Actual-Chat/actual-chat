namespace ActualChat.Mathematics;

public sealed class RunningAverage(double defaultValue)
{
    private double _sum;

    public int SampleCount { get; private set; } = 0;

    public double Value => SampleCount <= 0 ? defaultValue : _sum / SampleCount;

    public void Reset()
    {
        SampleCount = 0;
        _sum = 0;
    }

    public void AppendSample(double value)
    {
        _sum += value;
        SampleCount++;
    }
}
