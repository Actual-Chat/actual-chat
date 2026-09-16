namespace ActualChat.Diagnostics;

/// <summary>
/// Count, first, max and median over a small sample of durations - one utterance's worth of
/// per-stage latencies, summarized for a log line.
/// </summary>
public sealed class LatencyStats
{
    private readonly List<TimeSpan> _values = new();

    public IReadOnlyList<TimeSpan> Values => _values;
    public int Count => _values.Count;
    public TimeSpan? First => _values.Count > 0 ? _values[0] : null;
    public TimeSpan? Max { get; private set; }
    public TimeSpan? Median {
        get {
            if (_values.Count == 0)
                return null;

            var sorted = _values.Order().ToList();
            return sorted[sorted.Count / 2];
        }
    }

    public void Add(TimeSpan value)
    {
        _values.Add(value);
        if (Max == null || value > Max.Value)
            Max = value;
    }

    public override string ToString()
        => Count == 0
            ? "n=0"
            : $"p50 {Median!.Value.TotalSeconds:F1}s max {Max!.Value.TotalSeconds:F1}s (n={Count})";
}
