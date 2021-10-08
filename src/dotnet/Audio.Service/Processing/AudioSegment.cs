namespace ActualChat.Audio.Processing;

public record AudioSegment(
    int Index,
    StreamId StreamId,
    AudioRecord AudioRecord,
    AudioSource AudioSource,
    TimeSpan Offset,
    TimeSpan Duration);
