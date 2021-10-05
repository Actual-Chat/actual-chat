using ActualChat.Audio.WebM;

namespace ActualChat.Audio.Processing;

public record AudioStreamPart(
    int Index,
    StreamId StreamId,
    AudioRecord AudioRecord,
    AudioSource AudioSource,
    TimeSpan Offset,
    TimeSpan Duration);
