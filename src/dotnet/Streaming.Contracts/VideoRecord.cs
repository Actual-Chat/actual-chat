using ActualChat.Video;

namespace ActualChat.Streaming;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record VideoRecord(
    [property: DataMember, MemoryPackOrder(0), Key(0)] StreamId StreamId, // Ignored on upload
    [property: DataMember, MemoryPackOrder(1), Key(1)] Session Session,
    [property: DataMember, MemoryPackOrder(2), Key(2)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(3), Key(3)] double ClientStartOffset,
    [property: DataMember, MemoryPackOrder(4), Key(4)] VideoFormat Format,
    [property: DataMember, MemoryPackOrder(5), Key(5)] StreamKind StreamKind = StreamKind.Webcam
) : IHasId<StreamId>, IHasNodeRef
{
    StreamId IHasId<StreamId>.Id => StreamId;
    NodeRef IHasNodeRef.NodeRef => StreamId.NodeRef;
}
