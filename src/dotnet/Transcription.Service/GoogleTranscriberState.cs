namespace ActualChat.Transcription;

public class GoogleTranscriberState
{
    public Transcript Last { get; private set; } = new ();
    public Transcript LastFinal { get; private set; } = new ();

    public Transcript Update(Transcript next, bool isFinal)
    {
        Last = next;
        if (isFinal)
            LastFinal = next;
        return Last;
    }

    public Transcript AppendAlternative(string suffix, float suffixEndTime)
        => Update(LastFinal.WithSuffix(suffix, suffixEndTime), false);

    public Transcript AppendFinal(string suffix, float suffixEndTime)
        => Update(LastFinal.WithSuffix(suffix, suffixEndTime), true);

    public Transcript AppendFinal(string suffix, LinearMap suffixTextToTimeMap)
        => Update(LastFinal.WithSuffix(suffix, suffixTextToTimeMap), true);

    public Transcript Complete()
        => Update(LastFinal, false);
}
