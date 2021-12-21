namespace ActualChat.Audio.Processing;

public class ClosedAudioSegment
{
    public int Index { get; init; }
    public string StreamId { get; init; }
    public AudioRecord AudioRecord { get; init; }
    public TimeSpan Offset { get; init; }
    public TimeSpan Duration { get; init; }
    public AudioSource Audio { get; init; }
    public AudioSource AudioWithOffset { get; init; }

    public ClosedAudioSegment(OpenAudioSegment openAudioSegment, TimeSpan duration)
    {
        Index = openAudioSegment.Index;
        StreamId = openAudioSegment.StreamId;
        AudioRecord = openAudioSegment.AudioRecord;
        Offset = openAudioSegment.Offset;
        Duration = duration;
        Audio = openAudioSegment.Audio;
        AudioWithOffset = openAudioSegment.AudioWithOffset;
    }

    public async IAsyncEnumerable<AudioStreamPart> GetSegmentStream(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var parts = AudioWithOffset.GetStream(cancellationToken);
        await foreach (var part in parts.ConfigureAwait(false)) {
            if (part.Format != null)
                yield return part;
            else if (part.Frame != null) {
                if (part.Frame.Offset < Duration)
                    yield return part;
            }
        }
    }
}
