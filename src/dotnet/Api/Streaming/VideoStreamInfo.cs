using ActualChat.Video;
using MemoryPack;

namespace ActualChat.Streaming;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record VideoStreamInfo(
    [property: DataMember, MemoryPackOrder(0)] StreamId StreamId,
    [property: DataMember, MemoryPackOrder(1)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(2)] AuthorId AuthorId,
    [property: DataMember, MemoryPackOrder(3)] VideoFormat Format,
    [property: DataMember, MemoryPackOrder(4)] Moment StartedAt
);
