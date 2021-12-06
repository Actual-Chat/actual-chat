using Cysharp.Text;

namespace ActualChat.Transcription;

public class TranscriptUpdateExtractor
{
    private readonly List<Transcript> _finalTranscripts = new ();

    private Transcript _transcript = new ();
    private Transcript _previous = new ();

    public Transcript FinalizedPart { get; private set; } = new ();
    public Queue<TranscriptUpdate> Updates { get; } = new ();

    public void EnqueueUpdate(string text, double transcriptEndTime)
    {

        var plainText = text.Replace(",", "", StringComparison.InvariantCultureIgnoreCase);
        var plainPreviousText = _previous.Text.Replace(",", "", StringComparison.InvariantCultureIgnoreCase);
        if (plainText.Equals(plainPreviousText, StringComparison.InvariantCultureIgnoreCase))
            return;

        if (text.Length < _previous.Text.Length / 1.67)
            _transcript = _transcript.WithUpdate(new TranscriptUpdate(_previous));

        var updateText = ZString.Concat(_transcript.Text, text);
        var updateMap = new LinearMap(
            new []{ (double)_transcript.Text.Length, updateText.Length },
            new []{ _transcript.Duration, transcriptEndTime });

        Updates.Enqueue(new TranscriptUpdate(new Transcript {
            Text = updateText,
            TextToTimeMap = updateMap,
        }));

        _previous = new Transcript {
            Text = text,
            TextToTimeMap = updateMap,
        };
    }

    public void FinalizeWith(Transcript finalUpdate)
    {
        FinalizedPart = finalUpdate;
        _finalTranscripts.Add(finalUpdate);
    }

    public void Complete()
    {
        if (_finalTranscripts.Count == 0)
            return;

        _transcript = _finalTranscripts
            .Skip(1)
            .Aggregate(
                _finalTranscripts[0],
                (result, transcript) => result.WithUpdate(new TranscriptUpdate(transcript)));

        Updates.Enqueue(new TranscriptUpdate(_transcript));
    }
}
