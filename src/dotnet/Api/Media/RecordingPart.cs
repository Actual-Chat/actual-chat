namespace ActualChat.Media;

/// <summary>
/// Specifies the type of recording event.
/// </summary>
#pragma warning disable CA1028 // If possible, make the underlying type of RecordingEventKind System.Int32

public enum RecordingEventKind : byte
{
    Data = 0,
    Pause,
    Resume,
}

/// <summary>
/// Represents a part of a recording stream (data, pause, or resume event).
/// </summary>
[DataContract, MessagePackObject]
public partial class RecordingPart(RecordingEventKind eventKind)
{
    [DataMember(Order = 0), Key(0)]
    public RecordingEventKind EventKind { get; init; } = eventKind;

    [DataMember(Order = 1), Key(1)]
    public byte[]? Data { get; init; }
    [DataMember(Order = 2), Key(2)]
    public Moment? RecordedAt { get; init; } // Nullable is fine here
    [DataMember(Order = 3), Key(3)]
    public TimeSpan? Offset { get; init; } // Nullable is fine here
}
