namespace ActualChat.Audio;

public sealed record SpeechPaceHistogram(int BinWidth, IReadOnlyDictionary<int, long> Durations)
{
    public long TotalMilliseconds => Durations.Values.Sum();

    public static SpeechPaceHistogram Build(ReadOnlySpan<SpeechPaceSegment> segments, int binWidth = 5)
    {
        if (binWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(binWidth));

        var bins = new SortedDictionary<int, long>();
        foreach (var segment in segments) {
            segment.RequireValid();
            var index = checked((int)Math.Floor(segment.WordsPerMinute / binWidth));
            bins[index] = checked(bins.GetValueOrDefault(index) + segment.DurationMilliseconds);
        }
        return new SpeechPaceHistogram(binWidth, bins);
    }

    public static SpeechPaceDistribution Classify(
        ReadOnlySpan<SpeechPaceSegment> segments, double low, double high)
    {
        if (!double.IsFinite(low) || !double.IsFinite(high) || low < 0 || high < low)
            throw new ArgumentOutOfRangeException(nameof(low), "Invalid speech pace range.");

        long below = 0, within = 0, above = 0;
        foreach (var segment in segments) {
            segment.RequireValid();
            var pace = segment.WordsPerMinute;
            if (pace < low)
                below = checked(below + segment.DurationMilliseconds);
            else if (pace > high)
                above = checked(above + segment.DurationMilliseconds);
            else
                within = checked(within + segment.DurationMilliseconds);
        }
        return new SpeechPaceDistribution(below, within, above);
    }
}

[StructLayout(LayoutKind.Auto)]
public readonly record struct SpeechPaceDistribution(
    long BelowMilliseconds, long WithinMilliseconds, long AboveMilliseconds)
{
    public long TotalMilliseconds => checked(BelowMilliseconds + WithinMilliseconds + AboveMilliseconds);
    public double? WithinRate => TotalMilliseconds > 0 ? (double)WithinMilliseconds / TotalMilliseconds : null;
}
