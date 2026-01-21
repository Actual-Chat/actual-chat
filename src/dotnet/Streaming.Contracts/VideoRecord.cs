using MemoryPack;

namespace ActualChat.Streaming;

[DataContract, MemoryPackable]
public sealed partial record VideoRecord(
    [property: DataMember, MemoryPackOrder(0)] StreamId StreamId,
    [property: DataMember, MemoryPackOrder(1)] Session Session,
    [property: DataMember, MemoryPackOrder(2)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(3)] double ClientStartOffset,
    [property: DataMember, MemoryPackOrder(4)] string Codec,
    [property: DataMember, MemoryPackOrder(5)] int Width,
    [property: DataMember, MemoryPackOrder(6)] int Height,
    [property: DataMember, MemoryPackOrder(7)] StreamId? AudioStreamId // For sync
) : IHasId<StreamId>, IHasNodeRef
{
    StreamId IHasId<StreamId>.Id => StreamId;
    NodeRef IHasNodeRef.NodeRef => StreamId.NodeRef;
}

