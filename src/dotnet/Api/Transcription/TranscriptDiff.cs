namespace ActualChat.Transcription;

/// <summary>
/// Represents incremental changes to a <see cref="Transcript"/>.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record TranscriptDiff(
    [property: DataMember(Order = 0), Key(0)] StringDiff TextDiff,
    [property: DataMember(Order = 1), Key(1)] LinearMapDiff TimeMapDiff
) : ISanitized
{
    public static readonly TranscriptDiff None = new(StringDiff.None, LinearMapDiff.None);

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool IsNone => TextDiff.IsNone && TimeMapDiff.IsNone;

    [DataMember(Order = 2), Key(2)]
    public bool IsStable { get; init; }
    // Null = unchanged. Detected languages don't fit a text diff, so without this a folded diff
    // stream never knows them - and the dub's Decide can't tell a Russian speaker from an English one.
    [DataMember(Order = 3), Key(3)]
    public Language[]? Languages { get; init; }

    public static TranscriptDiff New(Transcript transcript, Transcript baseTranscript)
    {
        var textDiff = StringDiff.New(transcript.Text, baseTranscript.Text);
        var timeMapDiff = LinearMapDiff.New(transcript.TimeMap, baseTranscript.TimeMap, Transcript.TimeMapEpsilon);
        var languages = transcript.Languages.SequenceEqual(baseTranscript.Languages) ? null : transcript.Languages;
        return new TranscriptDiff(textDiff, timeMapDiff) {
            IsStable = transcript.IsStable,
            Languages = languages,
        };
    }

    public override string ToString()
    {
        if (IsNone)
            return "Δ()";

        var textDiff = Sanitizer.MaybeSanitize<Sanitizers.PrefixAndLengthHint>(TextDiff.ToString());
        return $"Δ({textDiff}, {TimeMapDiff})";
    }

    public Transcript ApplyTo(Transcript baseTranscript)
    {
        var languages = Languages ?? baseTranscript.Languages;
        if (IsNone)
            // An empty diff still carries the flags: "same text, now stable" is a real update
            return IsStable == baseTranscript.IsStable && ReferenceEquals(languages, baseTranscript.Languages)
                ? baseTranscript
                : baseTranscript with { IsStable = IsStable, Languages = languages };

        var text = baseTranscript.Text + TextDiff;
        var timeMap = TimeMapDiff.ApplyTo(baseTranscript.TimeMap, Transcript.TimeMapEpsilon.X);
        return new Transcript(text, timeMap, languages) { IsStable = IsStable };
    }

    // Operators

    public static Transcript operator +(Transcript baseTranscript, TranscriptDiff diff) => diff.ApplyTo(baseTranscript);
}
