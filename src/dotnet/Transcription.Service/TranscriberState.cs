namespace ActualChat.Transcription;

public class TranscriberState
{
    public Transcript Last { get; private set; } = Transcript.Empty;
    public Transcript LastStable { get; private set; } = Transcript.EmptyStable;

    public Transcript Update(Transcript next)
    {
        Last = next;
        if (next.IsStable)
            LastStable = next;
        return Last;
    }

    public Transcript AppendAlternative(string suffix, float? suffixEndTime)
        => Update(LastStable.WithSuffix(suffix, suffixEndTime, false));

    public Transcript AppendStable(string suffix, float? suffixEndTime)
        => Update(LastStable.WithSuffix(suffix, suffixEndTime, true));

    public Transcript AppendStable(string suffix, LinearMap suffixTextToTimeMap)
        => Update(LastStable.WithSuffix(suffix, suffixTextToTimeMap, true));

    public Transcript Complete()
        => Update(Last.WithFlags(TranscriptFlags.Stable));
}
