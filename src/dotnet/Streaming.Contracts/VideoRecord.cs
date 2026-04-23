using ActualChat.Video;

namespace ActualChat.Streaming;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record VideoRecord(
    [property: DataMember, MemoryPackOrder(0), NbKey(0)] StreamId StreamId, // Ignored on upload
    [property: DataMember, MemoryPackOrder(1), NbKey(1)] Session Session,
    [property: DataMember, MemoryPackOrder(2), NbKey(2)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(3), NbKey(3)] double ClientStartOffset,
    [property: DataMember, MemoryPackOrder(4), NbKey(4)] VideoFormat Format,
    [property: DataMember, MemoryPackOrder(5), NbKey(5)] StreamKind StreamKind = StreamKind.Webcam
) : IHasId<StreamId>, IHasNodeRef
{
    StreamId IHasId<StreamId>.Id => StreamId;
    NodeRef IHasNodeRef.NodeRef => StreamId.NodeRef;
}
