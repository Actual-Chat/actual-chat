namespace ActualChat.Media;

public enum RecordingEventKind : byte
{
    Data = 0,
    Pause,
    Resume,
}

[DataContract]
public class RecordingPart
{
    [DataMember(Order = 0)]
    public RecordingEventKind EventKind { get; init; }
    [DataMember(Order = 1)]
    public byte[]? Data { get; init; }
    [DataMember(Order = 2)]
    public Moment? RecordedAt { get; init; }
    [DataMember(Order = 3)]
    public TimeSpan? Offset { get; init; }

    public RecordingPart(RecordingEventKind eventKind)
        => EventKind = eventKind;
}
