using ActualChat.Video;

namespace ActualChat.Streaming;

[DataContract, MessagePackObject]
public sealed partial record VideoRecord(
    [property: DataMember, Key(0)] StreamId StreamId, // Ignored on upload
    [property: DataMember, Key(1)] Session Session,
    [property: DataMember, Key(2)] ChatId ChatId,
    [property: DataMember, Key(3)] double ClientStartAt, // Unix epoch (seconds, double)
    [property: DataMember, Key(4)] VideoFormat Format,
    [property: DataMember, Key(5)] VideoSourceKind SourceKind = VideoSourceKind.Camera
) : IHasId<StreamId>, IHasNodeRef
{
    StreamId IHasId<StreamId>.Id => StreamId;
    NodeRef IHasNodeRef.NodeRef => StreamId.NodeRef;
}
