namespace ActualChat.Audio.Processing;

public class ClosedAudioSegment
{
    public int Index { get; init; }
    public string StreamId { get; init; }
    public AudioRecord AudioRecord { get; init; }
    public Moment? RecordedAt { get; init; }
    public TimeSpan Duration { get; init; }
    public TimeSpan? VoiceDuration { get; init; }
    public AudioSource Audio { get; init; }

    public ClosedAudioSegment(OpenAudioSegment openAudioSegment, Moment? recordedAt, TimeSpan duration, TimeSpan? voiceDuration)
    {
        Index = openAudioSegment.Index;
        StreamId = openAudioSegment.StreamId;
        AudioRecord = openAudioSegment.AudioRecord;
        RecordedAt = recordedAt;
        Duration = duration;
        VoiceDuration = voiceDuration;
        Audio = openAudioSegment.Audio;
    }
}
