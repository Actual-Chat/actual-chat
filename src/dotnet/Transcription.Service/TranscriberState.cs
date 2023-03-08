namespace ActualChat.Transcription;

public class TranscriberState
{
    public Transcript Unstable { get; private set; } = Transcript.Empty;
    public Transcript Stable { get; private set; } = Transcript.Empty;

    public Transcript MarkStable()
        => Update(Unstable, true);

    public Transcript AppendUnstable(string suffix, float? suffixEndTime)
        => Update(Stable.WithSuffix(suffix, suffixEndTime), false);

    public Transcript AppendStable(string suffix, float? suffixEndTime)
        => Update(Stable.WithSuffix(suffix, suffixEndTime), true);

    public Transcript AppendStable(string suffix, LinearMap suffixTextToTimeMap)
        => Update(Stable.WithSuffix(suffix, suffixTextToTimeMap), true);

    // Private methods

    private Transcript Update(Transcript next, bool isStable)
    {
        Unstable = next;
        if (isStable)
            Stable = next;
        return Unstable;
    }
}
