namespace ActualChat.Media;

public enum RecordingEventKind : byte
{
    Pause = 0,
    Resume,
}

[DataContract]
public class RecordingPart
{
    [DataMember(Order = 0)]
    public byte[]? Data { get; init; }
    [DataMember(Order = 1)]
    public Moment? RecordedAt { get; init; }
    [DataMember(Order = 2)]
    public float? VoiceProbability { get; init; }
    [DataMember(Order = 3)]
    public RecordingEventKind? EventKind { get; init; }
}
