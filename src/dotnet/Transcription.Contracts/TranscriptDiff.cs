using MemoryPack;

namespace ActualChat.Transcription;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record TranscriptDiff(
    [property: DataMember(Order = 0), MemoryPackOrder(0)] StringDiff TextDiff,
    [property: DataMember(Order = 1), MemoryPackOrder(1)] LinearMapDiff TimeMapDiff)
{
    public static TranscriptDiff None { get; } = new(StringDiff.None, LinearMapDiff.None);

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsNone => TextDiff.IsNone && TimeMapDiff.IsNone;

    public static TranscriptDiff New(Transcript transcript, Transcript baseTranscript)
    {
        var textDiff = StringDiff.New(transcript.Text, baseTranscript.Text);
        var timeMapDiff = LinearMapDiff.New(transcript.TimeMap, baseTranscript.TimeMap, Transcript.TimeMapEpsilon);
        return new TranscriptDiff(textDiff, timeMapDiff);
    }

    public override string ToString()
        => IsNone ? "Δ()" : $"Δ({TextDiff}, {TimeMapDiff})";

    public Transcript ApplyTo(Transcript baseTranscript)
    {
        if (IsNone)
            return baseTranscript;

        var text = baseTranscript.Text + TextDiff;
        var timeMap = TimeMapDiff.ApplyTo(baseTranscript.TimeMap, Transcript.TimeMapEpsilon.X);
        return new Transcript(text, timeMap);
    }

    // Operators

    public static Transcript operator +(Transcript baseTranscript, TranscriptDiff diff) => diff.ApplyTo(baseTranscript);
}
