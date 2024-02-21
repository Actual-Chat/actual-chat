using ActualChat.Audio;
using ActualChat.Chat;

namespace ActualChat.Streaming;

public sealed class ClosedAudioSegment(
    OpenAudioSegment openSegment,
    Moment? recordedAt,
    TimeSpan duration,
    TimeSpan audibleDuration)
{
    public int Index { get; init; } = openSegment.Index;
    public string StreamId { get; init; } = openSegment.StreamId;
    public AudioRecord AudioRecord { get; init; } = openSegment.Record;
    public AudioSource Audio { get; init; } = openSegment.Source;
    public Moment? RecordedAt { get; init; } = recordedAt;
    public TimeSpan Duration { get; init; } = duration;
    public TimeSpan AudibleDuration { get; init; } = audibleDuration;
    public Author Author { get; init; } = openSegment.Author;
    public ApiArray<Language> Languages { get; init; } = openSegment.Languages;
}
