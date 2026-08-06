namespace ActualChat.Transcription;

// Chars rather than tokens because that's what Render trims to; CharsPerToken converts back for the
// provider-side limits these budgets have to stay under.

/// <summary>
/// How much context a transcriber configuration may be given, and which parts to build.
/// A null policy on <see cref="TranscriberInfo"/> means the transcriber takes no context at all.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record TranscriptionContextPolicy
{
    public const double CharsPerToken = 1.25;
    [DataMember(Order = 0), Key(0)]
    public int MaxChars { get; init; } = 80;
    [DataMember(Order = 1), Key(1)]
    public int MinChars { get; init; }
    [DataMember(Order = 2), Key(2)]
    public double CharsPerAudioSecond { get; init; }
    [DataMember(Order = 3), Key(3)]
    public int MaxPrefixEntries { get; init; } = 8;
    [DataMember(Order = 4), Key(4)]
    public bool IsSummaryIncluded { get; init; } = true;

    public int GetMaxChars(TimeSpan? audioDuration)
    {
        // Streaming has no duration to scale on, and a zero ratio is how a caller opts out of scaling.
        if (audioDuration is not { } duration || CharsPerAudioSecond <= 0)
            return MaxChars;

        return (int)Math.Clamp(duration.TotalSeconds * CharsPerAudioSecond, MinChars, MaxChars);
    }
}
