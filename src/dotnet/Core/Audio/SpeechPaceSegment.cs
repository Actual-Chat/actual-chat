namespace ActualChat.Audio;

[StructLayout(LayoutKind.Auto)]
public readonly record struct SpeechPaceSegment(Range<int> TextRange, Range<int> TimeRange, int Words)
{
    public int DurationMilliseconds => TimeRange.End - TimeRange.Start;
    public double WordsPerMinute => Words * 60_000d / DurationMilliseconds;

    public SpeechPaceSegment RequireValid()
    {
        if (Words <= 0 || TimeRange.Start < 0 || TimeRange.End <= TimeRange.Start
            || TextRange.Start < 0 || TextRange.End <= TextRange.Start)
            throw new ArgumentOutOfRangeException(nameof(SpeechPaceSegment), "Invalid speech pace segment.");

        return this;
    }
}
