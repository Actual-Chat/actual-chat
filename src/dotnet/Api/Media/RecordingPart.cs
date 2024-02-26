using MemoryPack;

namespace ActualChat.Media;

#pragma warning disable CA1028 // If possible, make the underlying type of RecordingEventKind System.Int32

public enum RecordingEventKind : byte
{
    Data = 0,
    Pause,
    Resume,
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class RecordingPart(RecordingEventKind eventKind)
{
    [DataMember(Order = 0), MemoryPackOrder(0)]
    public RecordingEventKind EventKind { get; init; } = eventKind;

    [DataMember(Order = 1), MemoryPackOrder(1)]
    public byte[]? Data { get; init; }
    [DataMember(Order = 2), MemoryPackOrder(2)]
    public Moment? RecordedAt { get; init; }
    [DataMember(Order = 3), MemoryPackOrder(3)]
    public TimeSpan? Offset { get; init; }
}
