using ActualChat.Video;

namespace ActualChat.Streaming;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial record VideoRecord(
    [property: DataMember, MemoryPackOrder(0)] StreamId StreamId, // Ignored on upload
    [property: DataMember, MemoryPackOrder(1)] Session Session,
    [property: DataMember, MemoryPackOrder(2)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(3)] double ClientStartOffset,
    [property: DataMember, MemoryPackOrder(4)] VideoFormat Format,
    [property: DataMember, MemoryPackOrder(5)] StreamKind StreamKind = StreamKind.Webcam,
    // Previous StreamId from the same sender session (WS reconnect, codec switch, reconfigure).
    // Propagated into VideoStreamInfo.ContinuationOf so viewers can soft-rebind.
    [property: DataMember, MemoryPackOrder(6)] StreamId? ContinuationOf = null
) : IHasId<StreamId>, IHasNodeRef
{
    StreamId IHasId<StreamId>.Id => StreamId;
    NodeRef IHasNodeRef.NodeRef => StreamId.NodeRef;
}
