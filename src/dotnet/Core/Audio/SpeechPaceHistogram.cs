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

[DataContract, MessagePackObject]
[StructLayout(LayoutKind.Auto)]
public readonly partial record struct SpeechPaceDistribution(
    [property: DataMember, Key(0)] long BelowMilliseconds,
    [property: DataMember, Key(1)] long WithinMilliseconds,
    [property: DataMember, Key(2)] long AboveMilliseconds)
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public long TotalMilliseconds => checked(BelowMilliseconds + WithinMilliseconds + AboveMilliseconds);
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public double? WithinRate => TotalMilliseconds > 0 ? (double)WithinMilliseconds / TotalMilliseconds : null;
}
