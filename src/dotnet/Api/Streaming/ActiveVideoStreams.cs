using MemoryPack;

namespace ActualChat.Streaming;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record ActiveVideoStreams(
    [property: DataMember, MemoryPackOrder(0)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(1)] long Version,
    [property: DataMember, MemoryPackOrder(2)] VideoStreamInfo[] Streams
);
