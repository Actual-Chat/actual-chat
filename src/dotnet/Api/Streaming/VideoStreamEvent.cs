using MemoryPack;

namespace ActualChat.Streaming;

public enum VideoStreamEventKind
{
    Started,
    Ended,
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record VideoStreamEvent(
    [property: DataMember, MemoryPackOrder(0)] VideoStreamEventKind Kind,
    [property: DataMember, MemoryPackOrder(1)] VideoStreamInfo StreamInfo
);
