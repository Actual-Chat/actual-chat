using Cysharp.Text;

namespace ActualChat.Transcription;

public class TranscriptUpdateExtractor
{
    public Transcript FinalizedPart { get; private set; } = new();
    public Transcript CurrentPart { get; private set; } = new();
    public Queue<TranscriptUpdate> Updates { get; } = new();
#if DEBUG
    public List<TranscriptUpdate> DebugUpdates { get; } = new();
    public Transcript DebugTranscript { get; private set; } = new();
#endif
    public int UpdateCount { get; private set; }

    public void UpdateCurrentPart(Transcript newCurrentPart)
    {
        var update = new TranscriptUpdate(CurrentPart);
        Updates.Enqueue(update);
        UpdateCount++;
        CurrentPart = newCurrentPart;
#if DEBUG
        DebugUpdates.Add(update);
        DebugTranscript = DebugTranscript.WithUpdate(update);
#endif
    }

    public void FinalizeCurrentPart()
    {
        if (CurrentPart.Text.IsNullOrEmpty())
            return; // Nothing to do
        FinalizedPart = new Transcript() {
            Text = ZString.Concat(FinalizedPart.Text, CurrentPart.Text, ' '),
            TextToTimeMap = FinalizedPart.TextToTimeMap.AppendOrUpdateTail(CurrentPart.TextToTimeMap),
        };
        FinalizedPart.TextToTimeMap.SourcePoints[^1] += 1;
        CurrentPart = NewCurrentPart();
        UpdateCurrentPart(CurrentPart);
    }

    private Transcript NewCurrentPart()
        => new() {
            TextToTimeMap = Transcript.EmptyMap.Offset(
                FinalizedPart.Text.Length,
                FinalizedPart.Duration),
        };
}
