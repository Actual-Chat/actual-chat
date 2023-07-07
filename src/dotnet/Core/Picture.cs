using MemoryPack;

namespace ActualChat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record Picture(
    [property: DataMember, MemoryPackOrder(0)] MediaContent? MediaContent,
    [property: DataMember, MemoryPackOrder(1)] string? ExternalUrl = null);
