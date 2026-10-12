namespace ActualChat.Audio;

[DataContract, MessagePackObject]
[StructLayout(LayoutKind.Auto)]
public readonly partial record struct SpeechPaceSegment(
    [property: DataMember, Key(0)] Range<int> TextRange,
    [property: DataMember, Key(1)] Range<int> TimeRange,
    [property: DataMember, Key(2)] int Words)
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public int DurationMilliseconds => TimeRange.End - TimeRange.Start;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public double WordsPerMinute => Words * 60_000d / DurationMilliseconds;

    public SpeechPaceSegment RequireValid()
    {
        if (Words <= 0 || TimeRange.Start < 0 || TimeRange.End <= TimeRange.Start
            || TextRange.Start < 0 || TextRange.End <= TextRange.Start)
            throw new ArgumentOutOfRangeException(nameof(SpeechPaceSegment), "Invalid speech pace segment.");

        return this;
    }
}
