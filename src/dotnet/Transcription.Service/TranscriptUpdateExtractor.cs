namespace ActualChat.Transcription;

public class TranscriptUpdateExtractor
{
    public Transcript Last { get; private set; } = new ();
    public Transcript LastFinal { get; private set; } = new ();

    public TranscriptUpdate UpdateTo(Transcript updated, bool isFinal = false)
    {
        var last = Last;
        Last = updated;
        if (isFinal)
            LastFinal = updated;
        return last.GetUpdateTo(updated);
    }

    public TranscriptUpdate AppendAlternative(string suffix, double suffixEndTime)
        => UpdateTo(LastFinal.WithSuffix(suffix, suffixEndTime));

    public TranscriptUpdate AppendFinal(string suffix, double suffixEndTime)
        => UpdateTo(LastFinal.WithSuffix(suffix, suffixEndTime), true);

    public TranscriptUpdate AppendFinal(string suffix, LinearMap suffixTextToTimeMap)
        => UpdateTo(LastFinal.WithSuffix(suffix, suffixTextToTimeMap), true);

    public TranscriptUpdate Complete()
        => UpdateTo(LastFinal);
}
