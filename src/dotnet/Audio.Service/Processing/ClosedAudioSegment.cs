namespace ActualChat.Audio.Processing;

public class ClosedAudioSegment
{
    public int Index { get; init; }
    public string StreamId { get; init; }
    public AudioRecord AudioRecord { get; init; }
    public TimeSpan Duration { get; init; }
    public AudioSource Audio { get; init; }
    public AudioMetadata Metadata { get; init; }

    public ClosedAudioSegment(OpenAudioSegment openAudioSegment, TimeSpan duration)
    {
        Index = openAudioSegment.Index;
        StreamId = openAudioSegment.StreamId;
        AudioRecord = openAudioSegment.AudioRecord;
        Duration = duration;
        Audio = openAudioSegment.Audio;
        Metadata = openAudioSegment.Audio.Metadata;
    }
}
