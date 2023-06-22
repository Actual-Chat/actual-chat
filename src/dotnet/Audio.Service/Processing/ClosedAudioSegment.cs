using ActualChat.Chat;

namespace ActualChat.Audio.Processing;

public sealed class ClosedAudioSegment
{
    public int Index { get; init; }
    public string StreamId { get; init; }
    public AudioRecord AudioRecord { get; init; }
    public AudioSource Audio { get; init; }
    public Moment? RecordedAt { get; init; }
    public TimeSpan Duration { get; init; }
    public TimeSpan AudibleDuration { get; init; }
    public Author Author { get; init; }
    public ApiArray<Language> Languages { get; init; }

    public ClosedAudioSegment(OpenAudioSegment openAudioSegment, Moment? recordedAt, TimeSpan duration, TimeSpan audibleDuration)
    {
        Index = openAudioSegment.Index;
        StreamId = openAudioSegment.StreamId;
        AudioRecord = openAudioSegment.AudioRecord;
        Audio = openAudioSegment.Audio;
        RecordedAt = recordedAt;
        Duration = duration;
        AudibleDuration = audibleDuration;
        Author = openAudioSegment.Author;
        Languages = openAudioSegment.Languages;
    }
}
