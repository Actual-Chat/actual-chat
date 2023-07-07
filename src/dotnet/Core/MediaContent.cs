using MemoryPack;

namespace ActualChat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record MediaContent(
    [property: DataMember, MemoryPackOrder(0)] MediaId MediaId,
    [property: DataMember, MemoryPackOrder(1)] string ContentId);
